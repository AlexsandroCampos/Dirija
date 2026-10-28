using Dirija.Core.Entities;
using Dirija.Core.Services;
using Dirija.Infrastructure.Persistence;
using Dirija.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

namespace Dirija.Tests;

public class BookingServiceTests : IAsyncLifetime
{
    private const int Student = 1;
    private const int OtherStudent = 2;
    private DomainFixture _f = null!;

    public async Task InitializeAsync() => _f = await DomainFixture.CreateAsync();
    public async Task DisposeAsync() => await _f.DisposeAsync();

    [Fact]
    public async Task HoldSlot_FreeSlot_CreatesPendingBookingAndHoldsSlot()
    {
        var slot = await _f.FirstFreeSlotAsync();
        var result = await _f.BookingService.HoldSlotAsync(Student, slot.Id);

        Assert.True(result.Success);
        Assert.Equal(BookingStatus.PendingPayment, result.Booking!.Status);
        Assert.Equal(SlotStatus.Held, slot.Status);
        Assert.Equal(_f.Clock.UtcNow.AddMinutes(10), slot.HeldUntil);
        Assert.Equal(result.Booking.Id, slot.BookingId);
    }

    [Fact]
    public async Task HoldSlot_SecondStudentOnSameSlot_Fails()
    {
        var slot = await _f.FirstFreeSlotAsync();
        Assert.True((await _f.BookingService.HoldSlotAsync(Student, slot.Id)).Success);

        var second = await _f.BookingService.HoldSlotAsync(OtherStudent, slot.Id);

        Assert.False(second.Success);
        Assert.Equal(BookingError.SlotUnavailable, second.Error);
    }

    [Fact]
    public async Task HoldSlot_SameStudentTwice_ReusesPendingBooking()
    {
        var slot = await _f.FirstFreeSlotAsync();
        var first = await _f.BookingService.HoldSlotAsync(Student, slot.Id);
        var again = await _f.BookingService.HoldSlotAsync(Student, slot.Id);

        Assert.True(again.Success);
        Assert.Equal(first.Booking!.Id, again.Booking!.Id);
    }

    [Fact]
    public async Task HoldSlot_StaleCopyOfSlot_IsRejectedByConcurrencyToken()
    {
        // Two requests load the same slot; the first one holds it, the second still has the old version.
        var slot = await _f.FirstFreeSlotAsync();
        await using var otherDb = _f.NewContext();
        var otherService = DomainFixture.CreateBookingService(otherDb, _f.Clock);
        _ = await otherDb.Slots.SingleAsync(s => s.Id == slot.Id); // tracked, Version = 0

        Assert.True((await _f.BookingService.HoldSlotAsync(Student, slot.Id)).Success);
        var late = await otherService.HoldSlotAsync(OtherStudent, slot.Id);

        Assert.Equal(BookingError.SlotUnavailable, late.Error);
        await using var check = _f.NewContext();
        Assert.Equal(1, await check.Bookings.CountAsync(b => b.SlotId == slot.Id));
    }

    [Fact]
    public async Task HoldSlot_ConcurrentAttemptsOnRealFile_OnlyOneSucceeds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dirija-race-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<DirijaDbContext>()
            .UseSqlite($"Data Source={path};Default Timeout=60").Options;
        try
        {
            var clock = new FakeClock(DomainFixture.DefaultNowUtc);
            int slotId;
            List<int> students;
            await using (var db = new DirijaDbContext(options))
            {
                await DbInitializer.InitializeAsync(db, clock, DomainFixture.Hasher);
                slotId = (await DomainFixture.FirstFreeSlotAsync(db, clock)).Id;
                var users = Enumerable.Range(0, 50).Select(i => new User
                {
                    Name = $"Aluno {i}", Email = $"race{i}@dirija.com", PasswordHash = "x"
                }).ToList();
                db.Users.AddRange(users);
                await db.SaveChangesAsync();
                students = users.Select(u => u.Id).ToList();
            }

            // 50 students, each with their own connection/DbContext, race for the same slot.
            var results = await Task.WhenAll(students.Select(studentId => Task.Run(async () =>
            {
                await using var db = new DirijaDbContext(options);
                var service = DomainFixture.CreateBookingService(db, clock);
                return (await service.HoldSlotAsync(studentId, slotId)).Success;
            })));

            Assert.Equal(1, results.Count(ok => ok));
            await using var check = new DirijaDbContext(options);
            Assert.Equal(1, await check.Bookings.CountAsync(b => b.SlotId == slotId));
            Assert.Equal(SlotStatus.Held, (await check.Slots.SingleAsync(s => s.Id == slotId)).Status);
        }
        finally
        {
            TempDb.Delete(path);
        }
    }

    [Fact]
    public async Task HoldSlot_AfterHoldExpires_SlotIsFreeAgain()
    {
        var slot = await _f.FirstFreeSlotAsync();
        var first = await _f.BookingService.HoldSlotAsync(Student, slot.Id);

        _f.Clock.Advance(TimeSpan.FromMinutes(11));
        var second = await _f.BookingService.HoldSlotAsync(OtherStudent, slot.Id);

        Assert.True(second.Success);
        Assert.Equal(BookingStatus.Canceled, first.Booking!.Status);
        Assert.Equal(second.Booking!.Id, slot.BookingId);
    }

    [Fact]
    public async Task HoldSlot_BookedOrBlockedOrPastSlot_Fails()
    {
        var now = _f.Clock.UtcNow;
        var booked = await _f.Db.Slots.FirstAsync(s => s.Status == SlotStatus.Booked && s.Start > now);
        var blocked = await _f.Db.Slots.FirstAsync(s => s.Status == SlotStatus.Blocked);
        var past = await _f.Db.Slots.FirstAsync(s => s.Start < now);

        Assert.Equal(BookingError.SlotUnavailable, (await _f.BookingService.HoldSlotAsync(Student, booked.Id)).Error);
        Assert.Equal(BookingError.SlotUnavailable, (await _f.BookingService.HoldSlotAsync(Student, blocked.Id)).Error);
        Assert.Equal(BookingError.SlotInPast, (await _f.BookingService.HoldSlotAsync(Student, past.Id)).Error);
        Assert.Equal(BookingError.SlotNotFound, (await _f.BookingService.HoldSlotAsync(Student, -1)).Error);
    }

    [Theory]
    [InlineData(90, 13.50, 76.50)]
    [InlineData(110, 16.50, 93.50)]
    [InlineData(85, 12.75, 72.25)]
    public void Split_UsesConfiguredTakeRate(decimal price, decimal fee, decimal net)
    {
        var (f, n) = _f.BookingService.CalculateSplit(price);
        Assert.Equal(fee, f);
        Assert.Equal(net, n);
        Assert.Equal(price, f + n);
    }

    [Fact]
    public async Task HoldSlot_StoresSplitOnBooking()
    {
        var slot = await _f.FirstFreeSlotAsync(1); // instructor 1 costs R$ 90
        var booking = (await _f.BookingService.HoldSlotAsync(Student, slot.Id)).Booking!;

        await using var check = _f.NewContext();
        var saved = await check.Bookings.SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(90m, saved.Price);
        Assert.Equal(13.50m, saved.PlatformFee);
        Assert.Equal(76.50m, saved.InstructorAmount);
    }

    // ------------------------------------------------------------- payment

    [Fact]
    public async Task ConfirmPayment_WithValidHold_BooksSlot()
    {
        var slot = await _f.FirstFreeSlotAsync();
        var booking = (await _f.BookingService.HoldSlotAsync(Student, slot.Id)).Booking!;

        var result = await _f.BookingService.ConfirmPaymentAsync(booking.Id, Student, PaymentMethod.Pix, "PIX");

        Assert.True(result.Success);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(SlotStatus.Booked, slot.Status);
        Assert.Null(slot.HeldUntil);
        Assert.NotNull(booking.PaidAt);
    }

    [Fact]
    public async Task ConfirmPayment_AfterHoldExpired_FailsAndReleasesSlot()
    {
        var slot = await _f.FirstFreeSlotAsync();
        var booking = (await _f.BookingService.HoldSlotAsync(Student, slot.Id)).Booking!;
        _f.Clock.Advance(TimeSpan.FromMinutes(10));

        var result = await _f.BookingService.ConfirmPaymentAsync(booking.Id, Student, PaymentMethod.Pix, "PIX");

        Assert.Equal(BookingError.HoldExpired, result.Error);
        Assert.Equal(BookingStatus.Canceled, booking.Status);
        Assert.Equal(SlotStatus.Available, slot.Status);
    }

    [Fact]
    public async Task ConfirmPayment_ByAnotherStudent_Fails()
    {
        var slot = await _f.FirstFreeSlotAsync();
        var booking = (await _f.BookingService.HoldSlotAsync(Student, slot.Id)).Booking!;

        var result = await _f.BookingService.ConfirmPaymentAsync(booking.Id, OtherStudent, PaymentMethod.Pix, "PIX");

        Assert.Equal(BookingError.NotOwner, result.Error);
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
        Assert.Equal(SlotStatus.Held, slot.Status);
    }

    // ------------------------------------------------------------- cancel

    [Fact]
    public async Task Cancel_ConfirmedBooking_ReleasesSlot()
    {
        var slot = await _f.FirstFreeSlotAsync();
        var booking = (await _f.BookingService.HoldSlotAsync(Student, slot.Id)).Booking!;
        await _f.BookingService.ConfirmPaymentAsync(booking.Id, Student, PaymentMethod.Pix, "PIX");

        var result = await _f.BookingService.CancelAsync(booking.Id, Student);

        Assert.True(result.Success);
        Assert.Equal(BookingStatus.Canceled, booking.Status);
        Assert.Equal(SlotStatus.Available, slot.Status);
        Assert.True((await _f.BookingService.HoldSlotAsync(OtherStudent, slot.Id)).Success);
    }

    [Fact]
    public async Task Cancel_ByAnotherStudent_Fails()
    {
        var slot = await _f.FirstFreeSlotAsync();
        var booking = (await _f.BookingService.HoldSlotAsync(Student, slot.Id)).Booking!;

        Assert.Equal(BookingError.NotOwner, (await _f.BookingService.CancelAsync(booking.Id, OtherStudent)).Error);
    }

    // ------------------------------------------------------------- reviews

    private async Task<Booking> CompletedBookingWithoutReviewAsync()
    {
        var list = await _f.BookingService.ListForStudentAsync(Student);
        var reviewed = await _f.BookingService.GetReviewedBookingIdsAsync(list.Select(b => b.Id));
        return list.First(b => b.Status == BookingStatus.Completed && !reviewed.Contains(b.Id));
    }

    [Fact]
    public async Task AddReview_UpdatesInstructorAverageAndCount()
    {
        var booking = await CompletedBookingWithoutReviewAsync();
        var instructor = await _f.Db.Instructors.SingleAsync(i => i.Id == booking.InstructorId);
        var (avg, count) = (instructor.AverageRating, instructor.ReviewCount);

        var result = await _f.BookingService.AddReviewAsync(booking.Id, Student, "Aluno Demo", 1, "Teste");

        Assert.True(result.Success);
        await using var check = _f.NewContext();
        var saved = await check.Instructors.SingleAsync(i => i.Id == instructor.Id);
        Assert.Equal(count + 1, saved.ReviewCount);
        Assert.Equal(Math.Round((avg * count + 1) / (count + 1), 2), saved.AverageRating);
        Assert.True(await check.Reviews.AnyAsync(r => r.BookingId == booking.Id && r.Comment == "Teste"));
    }

    [Fact]
    public async Task AddReview_Twice_Fails()
    {
        var booking = await CompletedBookingWithoutReviewAsync();
        Assert.True((await _f.BookingService.AddReviewAsync(booking.Id, Student, "Aluno Demo", 5, null)).Success);

        Assert.Equal(BookingError.AlreadyReviewed,
            (await _f.BookingService.AddReviewAsync(booking.Id, Student, "Aluno Demo", 5, null)).Error);
    }

    [Fact]
    public async Task AddReview_NotCompletedLesson_Fails()
    {
        var slot = await _f.FirstFreeSlotAsync();
        var booking = (await _f.BookingService.HoldSlotAsync(Student, slot.Id)).Booking!;
        await _f.BookingService.ConfirmPaymentAsync(booking.Id, Student, PaymentMethod.Pix, "PIX");

        Assert.Equal(BookingError.NotCompleted,
            (await _f.BookingService.AddReviewAsync(booking.Id, Student, "Aluno Demo", 5, null)).Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task AddReview_InvalidRating_Fails(int rating)
    {
        var booking = await CompletedBookingWithoutReviewAsync();
        Assert.Equal(BookingError.InvalidRating,
            (await _f.BookingService.AddReviewAsync(booking.Id, Student, "Aluno Demo", rating, null)).Error);
    }

    [Fact]
    public async Task ConfirmedLesson_BecomesCompleted_AfterItEnds()
    {
        var slot = await _f.FirstFreeSlotAsync();
        var booking = (await _f.BookingService.HoldSlotAsync(Student, slot.Id)).Booking!;
        await _f.BookingService.ConfirmPaymentAsync(booking.Id, Student, PaymentMethod.Pix, "PIX");

        _f.Clock.UtcNow = slot.End.AddMinutes(1);
        await _f.BookingService.ListForStudentAsync(Student);

        Assert.Equal(BookingStatus.Completed, booking.Status);
        Assert.True((await _f.BookingService.AddReviewAsync(booking.Id, Student, "Aluno Demo", 4, "Boa")).Success);
    }
}

public class PersistenceTests
{
    [Fact]
    public async Task Reservation_IsVisibleFromANewContext()
    {
        await using var f = await DomainFixture.CreateAsync();
        var slot = await f.FirstFreeSlotAsync();
        var booking = (await f.BookingService.HoldSlotAsync(1, slot.Id)).Booking!;
        await f.BookingService.ConfirmPaymentAsync(booking.Id, 1, PaymentMethod.CreditCard, "Cartão final 1111");

        await using var db = f.NewContext();
        var saved = await db.Bookings.SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Confirmed, saved.Status);
        Assert.Equal("Cartão final 1111", saved.PaymentLabel);
        Assert.Equal(DateTimeKind.Utc, saved.PaidAt!.Value.Kind);
        Assert.Equal(SlotStatus.Booked, (await db.Slots.SingleAsync(s => s.Id == slot.Id)).Status);
    }

    [Fact]
    public async Task Initialize_Twice_DoesNotDuplicateSeed()
    {
        await using var f = await DomainFixture.CreateAsync();
        var (users, instructors, slots) = (await f.Db.Users.CountAsync(), await f.Db.Instructors.CountAsync(), await f.Db.Slots.CountAsync());

        await using var db = f.NewContext();
        await DbInitializer.InitializeAsync(db, f.Clock, DomainFixture.Hasher);

        Assert.Equal(users, await db.Users.CountAsync());
        Assert.Equal(instructors, await db.Instructors.CountAsync());
        Assert.Equal(slots, await db.Slots.CountAsync());
    }

    [Fact]
    public async Task Initialize_DaysLater_TopsUpFutureSlots()
    {
        await using var f = await DomainFixture.CreateAsync();
        f.Clock.Advance(TimeSpan.FromDays(5));
        var lastLocalDay = Dirija.Core.Time.SaoPauloTime.Today(f.Clock.UtcNow).AddDays(SeedData.DaysAhead - 1);
        if (lastLocalDay.DayOfWeek == DayOfWeek.Sunday) lastLocalDay = lastLocalDay.AddDays(-1);
        var fromUtc = Dirija.Core.Time.SaoPauloTime.ToUtc(lastLocalDay);
        var toUtc = fromUtc.AddDays(1);

        await using var db = f.NewContext();
        await DbInitializer.InitializeAsync(db, f.Clock, DomainFixture.Hasher);

        var count = await db.Slots.CountAsync(s => s.InstructorId == 1 && s.Start >= fromUtc && s.Start < toUtc);
        Assert.True(count > 0);
    }
}
