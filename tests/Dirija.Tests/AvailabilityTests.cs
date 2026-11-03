using Dirija.Core.Entities;
using Dirija.Core.Services;
using Dirija.Core.Time;
using Dirija.Infrastructure.Persistence;
using Dirija.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

namespace Dirija.Tests;

/// <summary>
/// Agenda of the demo instructor Carlos Mendes (id 1, real weekly agenda: Mon–Fri 08–12 and 13–18, Sat 08–12).
/// The fixture clock is Monday 26/10/2026 07:30 in São Paulo.
/// </summary>
public class AvailabilityTests : IAsyncLifetime
{
    private const int Carlos = 1;
    private const int Student = 1;
    private DomainFixture _f = null!;
    public async Task InitializeAsync() => _f = await DomainFixture.CreateAsync();
    public async Task DisposeAsync() => await _f.DisposeAsync();

    private static DateTime Local(int day, int hour) => SaoPauloTime.ToUtc(new DateTime(2026, 10, day, hour, 0, 0));

    private Task<Slot> SlotAtAsync(DateTime startUtc) =>
        _f.Db.Slots.SingleAsync(s => s.InstructorId == Carlos && s.Start == startUtc);

    [Fact]
    public async Task Seed_RealAgenda_FollowsTheWeeklyAvailability_ForTheWholeWindow()
    {
        var slots = await _f.Db.Slots.Where(s => s.InstructorId == Carlos && s.IsActive && s.Start > _f.Clock.UtcNow).ToListAsync();
        var locals = slots.Select(s => SaoPauloTime.ToLocal(s.Start)).ToList();

        Assert.DoesNotContain(locals, l => l.DayOfWeek == DayOfWeek.Sunday);
        Assert.DoesNotContain(locals, l => l.Hour == 12);                                         // lunch break
        Assert.DoesNotContain(locals, l => l.DayOfWeek == DayOfWeek.Saturday && l.Hour >= 12);
        Assert.All(locals, l => Assert.InRange(l.Hour, 8, 17));
        var lastDay = SaoPauloTime.Today(_f.Clock.UtcNow).AddDays(DomainFixture.Agenda.WindowDays);
        Assert.All(locals, l => Assert.True(l.Date < lastDay));
        Assert.Contains(locals, l => l.Date >= lastDay.AddDays(-7)); // the window really reaches ~60 days
    }

    [Fact]
    public async Task SaveWeekly_RegeneratesFreeSlots_ButKeepsBookedLessons()
    {
        var booked = await _f.Db.Slots.SingleAsync(s => s.InstructorId == Carlos && s.Status == SlotStatus.Booked && s.Start > _f.Clock.UtcNow);

        var result = await _f.Availability.SaveWeeklyAsync(Carlos, [new AvailabilityRange(DayOfWeek.Wednesday, 9 * 60, 11 * 60)]);

        Assert.True(result.Success);
        await using var db = _f.NewContext();
        var active = await db.Slots.Where(s => s.InstructorId == Carlos && s.IsActive && s.Start > _f.Clock.UtcNow).ToListAsync();
        var free = active.Where(s => s.Status == SlotStatus.Available).Select(s => SaoPauloTime.ToLocal(s.Start)).ToList();
        Assert.NotEmpty(free);
        Assert.All(free, l => { Assert.Equal(DayOfWeek.Wednesday, l.DayOfWeek); Assert.InRange(l.Hour, 9, 10); });
        Assert.Contains(active, s => s.Id == booked.Id && s.Status == SlotStatus.Booked); // the booked lesson stays
    }

    [Fact]
    public void ValidateRanges_RejectsOverlapsHalfHoursAndOutOfDay()
    {
        Assert.Equal(AgendaError.InvalidRange, _f.Availability.ValidateRanges(
            [new(DayOfWeek.Monday, 8 * 60, 12 * 60), new(DayOfWeek.Monday, 11 * 60, 14 * 60)]));
        Assert.Equal(AgendaError.InvalidRange, _f.Availability.ValidateRanges([new(DayOfWeek.Monday, 8 * 60 + 30, 12 * 60)]));
        Assert.Equal(AgendaError.InvalidRange, _f.Availability.ValidateRanges([new(DayOfWeek.Monday, 4 * 60, 8 * 60)]));
        Assert.Equal(AgendaError.InvalidRange, _f.Availability.ValidateRanges([new(DayOfWeek.Monday, 12 * 60, 10 * 60)]));
        Assert.Equal(AgendaError.None, _f.Availability.ValidateRanges(
            [new(DayOfWeek.Monday, 8 * 60, 12 * 60), new(DayOfWeek.Monday, 13 * 60, 18 * 60)]));
    }

    [Fact]
    public async Task TimeOff_BlocksFreeSlots_AndRemovingItFreesThemAgain()
    {
        var start = Local(27, 13);
        var end = Local(27, 15); // Tuesday 13:00–15:00 (medical exam)

        var added = await _f.Availability.AddTimeOffAsync(Carlos, start, end, "exame médico");

        Assert.True(added.Success);
        Assert.Equal(SlotStatus.Blocked, (await SlotAtAsync(Local(27, 13))).Status);
        Assert.Equal(SlotStatus.Blocked, (await SlotAtAsync(Local(27, 14))).Status);
        Assert.Equal(SlotStatus.Available, (await SlotAtAsync(Local(27, 15))).Status);
        var bookable = await _f.BookingService.GetBookableSlotsAsync(Carlos, Local(28, 0));
        Assert.DoesNotContain(bookable, s => s.Start >= start && s.Start < end);

        Assert.True((await _f.Availability.RemoveTimeOffAsync(Carlos, added.TimeOff!.Id)).Success);
        Assert.Equal(SlotStatus.Available, (await SlotAtAsync(Local(27, 13))).Status);
    }

    [Fact]
    public async Task TimeOff_KeepsBookedLessons_AndListsThemAsAffected()
    {
        var slot = await _f.Db.Slots.SingleAsync(s => s.InstructorId == Carlos && s.Start == Local(27, 16));
        var booking = await _f.BookAsync(Student, slot);

        var affected = await _f.Availability.GetAffectedBookingsAsync(Carlos, Local(27, 0), Local(28, 0));
        Assert.Contains(affected, a => a.Booking.Id == booking.Id);

        Assert.True((await _f.Availability.AddTimeOffAsync(Carlos, Local(27, 0), Local(28, 0), "folga")).Success);
        await using var db = _f.NewContext();
        Assert.Equal(SlotStatus.Booked, (await db.Slots.SingleAsync(s => s.Id == slot.Id)).Status);
        Assert.Equal(BookingStatus.Confirmed, (await db.Bookings.SingleAsync(b => b.Id == booking.Id)).Status);
    }

    [Fact]
    public async Task StudentCancelInsideATimeOff_LeavesTheSlotBlocked()
    {
        var slot = await _f.Db.Slots.SingleAsync(s => s.InstructorId == Carlos && s.Start == Local(28, 9));
        var booking = await _f.BookAsync(Student, slot);
        await _f.Availability.AddTimeOffAsync(Carlos, Local(28, 0), Local(29, 0), null);

        Assert.True((await _f.BookingService.CancelAsync(booking.Id, Student)).Success);

        await using var db = _f.NewContext();
        Assert.Equal(SlotStatus.Blocked, (await db.Slots.SingleAsync(s => s.Id == slot.Id)).Status);
    }

    [Fact]
    public async Task NewDaysEnteringTheWindow_RespectAnExistingTimeOff()
    {
        // A vacation registered far ahead (day 70) applies when that day enters the 60-day window.
        var today = SaoPauloTime.Today(_f.Clock.UtcNow);
        var day = today.AddDays(70);
        while (day.DayOfWeek is DayOfWeek.Sunday or DayOfWeek.Saturday) day = day.AddDays(1);
        Assert.True((await _f.Availability.AddTimeOffAsync(Carlos, SaoPauloTime.ToUtc(day), SaoPauloTime.ToUtc(day.AddDays(1)), "férias")).Success);

        _f.Clock.Advance(TimeSpan.FromDays(15));
        await _f.Availability.SyncAsync(Carlos);

        await using var db = _f.NewContext();
        var from = SaoPauloTime.ToUtc(day);
        var to = SaoPauloTime.ToUtc(day.AddDays(1));
        var slots = await db.Slots.Where(s => s.InstructorId == Carlos && s.Start >= from && s.Start < to).ToListAsync();
        Assert.NotEmpty(slots);
        Assert.All(slots, s => Assert.Equal(SlotStatus.Blocked, s.Status));
    }

    [Fact]
    public async Task BlockAndUnblockSingleSlot_AndUnblockInsideAPeriodIsRefused()
    {
        var single = await SlotAtAsync(Local(29, 8));
        Assert.True((await _f.Availability.BlockSlotAsync(Carlos, single.Id)).Success);
        Assert.Equal(AgendaError.AlreadyBlocked, (await _f.Availability.BlockSlotAsync(Carlos, single.Id)).Error);
        Assert.True((await _f.Availability.UnblockSlotAsync(Carlos, single.Id)).Success);
        Assert.Equal(SlotStatus.Available, (await SlotAtAsync(Local(29, 8))).Status);

        await _f.Availability.AddTimeOffAsync(Carlos, Local(30, 8), Local(30, 12), null);
        var inside = await SlotAtAsync(Local(30, 9));
        Assert.Equal(AgendaError.CoveredByPeriod, (await _f.Availability.UnblockSlotAsync(Carlos, inside.Id)).Error);
    }

    [Fact]
    public async Task BlockSlot_OfAnotherInstructor_IsNotFound()
    {
        var other = await _f.Db.Slots.FirstAsync(s => s.InstructorId == 2 && s.Status == SlotStatus.Available && s.Start > _f.Clock.UtcNow);
        Assert.Equal(AgendaError.NotFound, (await _f.Availability.BlockSlotAsync(Carlos, other.Id)).Error);
    }

    [Fact]
    public async Task ExtraSlot_OnSunday_IsBookable_AndCannotBeDuplicated()
    {
        var sunday = new DateTime(2026, 11, 1, 10, 0, 0);

        var added = await _f.Availability.AddExtraSlotAsync(Carlos, sunday);

        Assert.True(added.Success);
        Assert.True(added.Slot!.IsManual);
        Assert.Equal(AgendaError.AlreadyExists, (await _f.Availability.AddExtraSlotAsync(Carlos, sunday)).Error);
        Assert.True((await _f.BookingService.HoldSlotAsync(Student, added.Slot.Id)).Success);

        // A later change of the weekly availability does not remove a manual extra slot.
        await _f.Availability.SyncAsync(Carlos);
        Assert.True((await _f.Db.Slots.SingleAsync(s => s.Id == added.Slot.Id)).IsActive);
    }

    [Fact]
    public async Task ExtraSlot_OutsideWindowOrHalfHour_IsRejected()
    {
        var today = SaoPauloTime.Today(_f.Clock.UtcNow);
        Assert.Equal(AgendaError.OutsideWindow, (await _f.Availability.AddExtraSlotAsync(Carlos, today.AddDays(61).AddHours(10))).Error);
        Assert.Equal(AgendaError.InvalidRange, (await _f.Availability.AddExtraSlotAsync(Carlos, today.AddDays(6).AddHours(10).AddMinutes(30))).Error);
        Assert.Equal(AgendaError.InPast, (await _f.Availability.AddExtraSlotAsync(Carlos, today.AddHours(7))).Error);
    }

    [Fact]
    public async Task TimeOff_TooLongOrInvertedOrPast_IsRejected()
    {
        var now = _f.Clock.UtcNow;
        Assert.Equal(AgendaError.TooLong, (await _f.Availability.AddTimeOffAsync(Carlos, now.AddDays(1), now.AddDays(100), null)).Error);
        Assert.Equal(AgendaError.InvalidRange, (await _f.Availability.AddTimeOffAsync(Carlos, now.AddDays(2), now.AddDays(1), null)).Error);
        Assert.Equal(AgendaError.InPast, (await _f.Availability.AddTimeOffAsync(Carlos, now.AddDays(-2), now.AddDays(-1), null)).Error);
        Assert.Equal(AgendaError.NoteTooLong, (await _f.Availability.AddTimeOffAsync(Carlos, now.AddDays(1), now.AddDays(2), new string('x', 101))).Error);
    }

    [Fact]
    public async Task BlockAndReserveAtTheSameMoment_OnlyOneWins()
    {
        // The instructor's request loaded the slot before the student held it (stale version).
        var slot = await _f.FirstFreeSlotAsync(Carlos);
        await using var instructorDb = _f.NewContext();
        var instructorAgenda = DomainFixture.CreateAvailability(instructorDb, _f.Clock);
        _ = await instructorDb.Slots.SingleAsync(s => s.Id == slot.Id);

        Assert.True((await _f.BookingService.HoldSlotAsync(Student, slot.Id)).Success);
        var block = await instructorAgenda.BlockSlotAsync(Carlos, slot.Id);

        Assert.Equal(AgendaError.Conflict, block.Error);
        await using var check = _f.NewContext();
        Assert.Equal(SlotStatus.Held, (await check.Slots.SingleAsync(s => s.Id == slot.Id)).Status);
        Assert.False(await check.TimeOffs.AnyAsync(t => t.InstructorId == Carlos && t.Start == slot.Start)); // rolled back
    }

    [Fact]
    public async Task ConcurrentSyncsOnRealFile_NeverDuplicateSlots()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dirija-sync-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<DirijaDbContext>().UseSqlite($"Data Source={path};Default Timeout=60").Options;
        try
        {
            var clock = new FakeClock(DomainFixture.DefaultNowUtc);
            await using (var db = new DirijaDbContext(options))
            {
                await DbInitializer.InitializeAsync(db, clock, DomainFixture.Hasher);
                // Brand-new weekly availability for Carlos with no slots generated yet.
                db.Slots.RemoveRange(db.Slots.Where(s => s.InstructorId == Carlos && s.BookingId == null &&
                                                         !db.Bookings.Any(b => b.SlotId == s.Id)));
                await db.SaveChangesAsync();
            }

            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                await using var db = new DirijaDbContext(options);
                return await DomainFixture.CreateAvailability(db, clock).SyncAsync(Carlos);
            })));

            Assert.Contains(true, results);
            await using var check = new DirijaDbContext(options);
            var starts = await check.Slots.Where(s => s.InstructorId == Carlos).Select(s => s.Start).ToListAsync();
            Assert.NotEmpty(starts);
            Assert.Equal(starts.Count, starts.Distinct().Count());
        }
        finally
        {
            TempDb.Delete(path);
        }
    }
}

public class CancellationTests : IAsyncLifetime
{
    private const int Carlos = 1;
    private const int Fernanda = 2;
    private const int Student = 1;
    private DomainFixture _f = null!;
    public async Task InitializeAsync() => _f = await DomainFixture.CreateAsync();
    public async Task DisposeAsync() => await _f.DisposeAsync();

    [Fact]
    public async Task Student_24HoursOrMoreBefore_GetsFullRefund_AndSlotIsFreed()
    {
        var slot = await _f.FreeSlotAtLeastAsync(30, Carlos);
        var booking = await _f.BookAsync(Student, slot);

        var result = await _f.BookingService.CancelAsync(booking.Id, Student);

        Assert.True(result.Success);
        Assert.Equal(booking.Price, booking.RefundAmount);
        Assert.Equal(CanceledBy.Student, booking.CanceledBy);
        Assert.Equal(SlotStatus.Available, slot.Status);
    }

    [Fact]
    public async Task Student_LessThan24HoursBefore_CanCancel_ButWithoutRefund()
    {
        var slot = await _f.FirstFreeSlotAsync(Carlos); // today, about 6.5 h ahead
        Assert.True(slot.Start - _f.Clock.UtcNow < TimeSpan.FromHours(24));
        var booking = await _f.BookAsync(Student, slot);

        var policy = _f.BookingService.StudentCancelPolicy(booking, slot);
        var result = await _f.BookingService.CancelAsync(booking.Id, Student);

        Assert.True(policy.CanCancel);
        Assert.Equal(0m, policy.Refund);
        Assert.True(result.Success);
        Assert.Equal(0m, booking.RefundAmount);
    }

    [Fact]
    public async Task Student_PendingPayment_CancelsWithNothingToRefund()
    {
        var slot = await _f.FirstFreeSlotAsync(Carlos);
        var booking = (await _f.BookingService.HoldSlotAsync(Student, slot.Id)).Booking!;

        Assert.True((await _f.BookingService.CancelAsync(booking.Id, Student)).Success);
        Assert.Null(booking.RefundAmount);
    }

    [Fact]
    public async Task Student_AfterTheLessonStarted_CannotCancel()
    {
        var slot = await _f.FirstFreeSlotAsync(Carlos);
        var booking = await _f.BookAsync(Student, slot);
        _f.Clock.UtcNow = slot.Start.AddMinutes(5);

        Assert.Equal(BookingError.InvalidState, (await _f.BookingService.CancelAsync(booking.Id, Student)).Error);
    }

    [Fact]
    public async Task Hold_SoonerThanMinimumNotice_IsRefused()
    {
        var soon = await _f.Db.Slots.FirstAsync(s => s.InstructorId == Carlos && s.Status == SlotStatus.Available &&
                                                     s.Start > _f.Clock.UtcNow && s.Start < _f.Clock.UtcNow.AddHours(6));

        Assert.Equal(BookingError.TooLate, (await _f.BookingService.HoldSlotAsync(Student, soon.Id)).Error);
        Assert.DoesNotContain(await _f.BookingService.GetBookableSlotsAsync(Carlos, _f.Clock.UtcNow.AddDays(1)), s => s.Id == soon.Id);
    }

    [Fact]
    public async Task Instructor_Cancels_StudentGetsFullRefundAndReason_SlotStaysBlocked()
    {
        var slot = await _f.FirstFreeSlotAsync(Carlos);
        var booking = await _f.BookAsync(Student, slot);

        var result = await _f.BookingService.CancelByInstructorAsync(booking.Id, Carlos, "carro na oficina");

        Assert.True(result.Success);
        await using var db = _f.NewContext();
        var saved = await db.Bookings.SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Canceled, saved.Status);
        Assert.Equal(CanceledBy.Instructor, saved.CanceledBy);
        Assert.Equal(saved.Price, saved.RefundAmount);
        Assert.Contains("carro na oficina", saved.CancelReason);
        Assert.Equal(SlotStatus.Blocked, (await db.Slots.SingleAsync(s => s.Id == slot.Id)).Status);
        Assert.Equal(BookingError.SlotUnavailable, (await _f.BookingService.HoldSlotAsync(2, slot.Id)).Error);
    }

    [Fact]
    public async Task Instructor_CannotCancelAnotherInstructorsLesson_NorWithoutReason()
    {
        var slot = await _f.FirstFreeSlotAsync(Carlos);
        var booking = await _f.BookAsync(Student, slot);

        Assert.Equal(BookingError.NotOwner, (await _f.BookingService.CancelByInstructorAsync(booking.Id, Fernanda, "motivo qualquer")).Error);
        Assert.Equal(BookingError.InvalidReason, (await _f.BookingService.CancelByInstructorAsync(booking.Id, Carlos, " ")).Error);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public async Task PendingInstructor_CannotBeBooked()
    {
        var tiago = await _f.Db.Instructors.SingleAsync(i => i.Status == InstructorStatus.Pending);
        var slot = await _f.FirstFreeSlotAsync(tiago.Id);

        Assert.Equal(BookingError.SlotNotFound, (await _f.BookingService.HoldSlotAsync(Student, slot.Id)).Error);
    }
}
