using Dirija.Core.Entities;
using Dirija.Core.Services;

namespace Dirija.Tests;

public class BookingServiceTests
{
    private const int Student = 1;
    private const int OtherStudent = 2;
    private readonly DomainFixture _f = new();

    [Fact]
    public void HoldSlot_FreeSlot_CreatesPendingBookingAndHoldsSlot()
    {
        var slot = _f.FirstFreeSlot();
        var result = _f.BookingService.HoldSlot(Student, slot.Id);

        Assert.True(result.Success);
        Assert.Equal(BookingStatus.PendingPayment, result.Booking!.Status);
        Assert.Equal(SlotStatus.Held, slot.Status);
        Assert.Equal(_f.Clock.Now.AddMinutes(10), slot.HeldUntil);
        Assert.Equal(result.Booking.Id, slot.BookingId);
    }

    [Fact]
    public void HoldSlot_SecondStudentOnSameSlot_Fails()
    {
        var slot = _f.FirstFreeSlot();
        Assert.True(_f.BookingService.HoldSlot(Student, slot.Id).Success);

        var second = _f.BookingService.HoldSlot(OtherStudent, slot.Id);

        Assert.False(second.Success);
        Assert.Equal(BookingError.SlotUnavailable, second.Error);
    }

    [Fact]
    public void HoldSlot_SameStudentTwice_ReusesPendingBooking()
    {
        var slot = _f.FirstFreeSlot();
        var first = _f.BookingService.HoldSlot(Student, slot.Id);
        var again = _f.BookingService.HoldSlot(Student, slot.Id);

        Assert.True(again.Success);
        Assert.Equal(first.Booking!.Id, again.Booking!.Id);
    }

    [Fact]
    public void HoldSlot_ConcurrentAttempts_OnlyOneSucceeds()
    {
        var slot = _f.FirstFreeSlot();
        var successes = 0;

        Parallel.For(0, 50, i =>
        {
            // 50 different students race for the same slot.
            if (_f.BookingService.HoldSlot(1000 + i, slot.Id).Success) Interlocked.Increment(ref successes);
        });

        Assert.Equal(1, successes);
        Assert.Single(_f.Store.Bookings.Values, b => b.SlotId == slot.Id && b.Status == BookingStatus.PendingPayment);
    }

    [Fact]
    public void HoldSlot_AfterHoldExpires_SlotIsFreeAgain()
    {
        var slot = _f.FirstFreeSlot();
        var first = _f.BookingService.HoldSlot(Student, slot.Id);

        _f.Clock.Advance(TimeSpan.FromMinutes(11));
        var second = _f.BookingService.HoldSlot(OtherStudent, slot.Id);

        Assert.True(second.Success);
        Assert.Equal(BookingStatus.Canceled, first.Booking!.Status);
        Assert.Equal(second.Booking!.Id, slot.BookingId);
    }

    [Fact]
    public void HoldSlot_BookedOrBlockedOrPastSlot_Fails()
    {
        var booked = _f.Store.Slots.Values.First(s => s.Status == SlotStatus.Booked && s.Start > _f.Clock.Now);
        var blocked = _f.Store.Slots.Values.First(s => s.Status == SlotStatus.Blocked);
        var past = _f.Store.Slots.Values.First(s => s.Start < _f.Clock.Now);

        Assert.Equal(BookingError.SlotUnavailable, _f.BookingService.HoldSlot(Student, booked.Id).Error);
        Assert.Equal(BookingError.SlotUnavailable, _f.BookingService.HoldSlot(Student, blocked.Id).Error);
        Assert.Equal(BookingError.SlotInPast, _f.BookingService.HoldSlot(Student, past.Id).Error);
        Assert.Equal(BookingError.SlotNotFound, _f.BookingService.HoldSlot(Student, -1).Error);
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
    public void HoldSlot_StoresSplitOnBooking()
    {
        var slot = _f.FirstFreeSlot(1); // instructor 1 costs R$ 90
        var booking = _f.BookingService.HoldSlot(Student, slot.Id).Booking!;

        Assert.Equal(90m, booking.Price);
        Assert.Equal(13.50m, booking.PlatformFee);
        Assert.Equal(76.50m, booking.InstructorAmount);
    }

    // ------------------------------------------------------------- payment

    [Fact]
    public void ConfirmPayment_WithValidHold_BooksSlot()
    {
        var slot = _f.FirstFreeSlot();
        var booking = _f.BookingService.HoldSlot(Student, slot.Id).Booking!;

        var result = _f.BookingService.ConfirmPayment(booking.Id, Student, PaymentMethod.Pix, "PIX");

        Assert.True(result.Success);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(SlotStatus.Booked, slot.Status);
        Assert.Null(slot.HeldUntil);
        Assert.NotNull(booking.PaidAt);
    }

    [Fact]
    public void ConfirmPayment_AfterHoldExpired_FailsAndReleasesSlot()
    {
        var slot = _f.FirstFreeSlot();
        var booking = _f.BookingService.HoldSlot(Student, slot.Id).Booking!;
        _f.Clock.Advance(TimeSpan.FromMinutes(10));

        var result = _f.BookingService.ConfirmPayment(booking.Id, Student, PaymentMethod.Pix, "PIX");

        Assert.Equal(BookingError.HoldExpired, result.Error);
        Assert.Equal(BookingStatus.Canceled, booking.Status);
        Assert.Equal(SlotStatus.Available, slot.Status);
    }

    [Fact]
    public void ConfirmPayment_ByAnotherStudent_Fails()
    {
        var slot = _f.FirstFreeSlot();
        var booking = _f.BookingService.HoldSlot(Student, slot.Id).Booking!;

        var result = _f.BookingService.ConfirmPayment(booking.Id, OtherStudent, PaymentMethod.Pix, "PIX");

        Assert.Equal(BookingError.NotOwner, result.Error);
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
        Assert.Equal(SlotStatus.Held, slot.Status);
    }

    // ------------------------------------------------------------- cancel

    [Fact]
    public void Cancel_ConfirmedBooking_ReleasesSlot()
    {
        var slot = _f.FirstFreeSlot();
        var booking = _f.BookingService.HoldSlot(Student, slot.Id).Booking!;
        _f.BookingService.ConfirmPayment(booking.Id, Student, PaymentMethod.Pix, "PIX");

        var result = _f.BookingService.Cancel(booking.Id, Student);

        Assert.True(result.Success);
        Assert.Equal(BookingStatus.Canceled, booking.Status);
        Assert.Equal(SlotStatus.Available, slot.Status);
        Assert.True(_f.BookingService.HoldSlot(OtherStudent, slot.Id).Success);
    }

    [Fact]
    public void Cancel_ByAnotherStudent_Fails()
    {
        var slot = _f.FirstFreeSlot();
        var booking = _f.BookingService.HoldSlot(Student, slot.Id).Booking!;

        Assert.Equal(BookingError.NotOwner, _f.BookingService.Cancel(booking.Id, OtherStudent).Error);
    }

    // ------------------------------------------------------------- reviews

    private Booking CompletedBookingWithoutReview() =>
        _f.BookingService.ListForStudent(Student)
            .First(b => b.Status == BookingStatus.Completed && !_f.BookingService.HasReview(b.Id));

    [Fact]
    public void AddReview_UpdatesInstructorAverageAndCount()
    {
        var booking = CompletedBookingWithoutReview();
        var instructor = _f.Instructors.GetById(booking.InstructorId)!;
        var (avg, count) = (instructor.AverageRating, instructor.ReviewCount);

        var result = _f.BookingService.AddReview(booking.Id, Student, "Aluno Demo", 1, "Teste");

        Assert.True(result.Success);
        Assert.Equal(count + 1, instructor.ReviewCount);
        Assert.Equal(Math.Round((avg * count + 1) / (count + 1), 2), instructor.AverageRating);
        Assert.Contains(_f.Reviews.GetByInstructor(instructor.Id), r => r.BookingId == booking.Id && r.Comment == "Teste");
    }

    [Fact]
    public void AddReview_Twice_Fails()
    {
        var booking = CompletedBookingWithoutReview();
        Assert.True(_f.BookingService.AddReview(booking.Id, Student, "Aluno Demo", 5, null).Success);

        Assert.Equal(BookingError.AlreadyReviewed, _f.BookingService.AddReview(booking.Id, Student, "Aluno Demo", 5, null).Error);
    }

    [Fact]
    public void AddReview_NotCompletedLesson_Fails()
    {
        var slot = _f.FirstFreeSlot();
        var booking = _f.BookingService.HoldSlot(Student, slot.Id).Booking!;
        _f.BookingService.ConfirmPayment(booking.Id, Student, PaymentMethod.Pix, "PIX");

        Assert.Equal(BookingError.NotCompleted, _f.BookingService.AddReview(booking.Id, Student, "Aluno Demo", 5, null).Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void AddReview_InvalidRating_Fails(int rating)
    {
        var booking = CompletedBookingWithoutReview();
        Assert.Equal(BookingError.InvalidRating, _f.BookingService.AddReview(booking.Id, Student, "Aluno Demo", rating, null).Error);
    }

    [Fact]
    public void ConfirmedLesson_BecomesCompleted_AfterItEnds()
    {
        var slot = _f.FirstFreeSlot();
        var booking = _f.BookingService.HoldSlot(Student, slot.Id).Booking!;
        _f.BookingService.ConfirmPayment(booking.Id, Student, PaymentMethod.Pix, "PIX");

        _f.Clock.Now = slot.End.AddMinutes(1);
        _f.BookingService.ListForStudent(Student);

        Assert.Equal(BookingStatus.Completed, booking.Status);
        Assert.True(_f.BookingService.AddReview(booking.Id, Student, "Aluno Demo", 4, "Boa").Success);
    }
}
