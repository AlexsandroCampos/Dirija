using Dirija.Core.Abstractions;
using Dirija.Core.Entities;

namespace Dirija.Core.Services;

public class BookingOptions
{
    public const string SectionName = "Booking";
    /// <summary>Platform take rate in percent (spec does not define it; 15% for the demo).</summary>
    public decimal PlatformFeePercent { get; set; } = 15m;
    public int HoldMinutes { get; set; } = 10;
}

public enum BookingError
{
    None,
    SlotNotFound,
    SlotUnavailable,
    SlotInPast,
    BookingNotFound,
    NotOwner,
    HoldExpired,
    InvalidState,
    NotCompleted,
    AlreadyReviewed,
    InvalidRating
}

public record BookingResult(BookingError Error, Booking? Booking = null)
{
    public bool Success => Error == BookingError.None;
    public static BookingResult Ok(Booking booking) => new(BookingError.None, booking);
    public static BookingResult Fail(BookingError error, Booking? booking = null) => new(error, booking);
}

/// <summary>
/// Reservation workflow. All state transitions run under a single lock, which guarantees that a slot
/// can never be held or booked twice (anti-overbooking). With PostgreSQL this becomes a transaction
/// with SELECT ... FOR UPDATE (or an exclusion constraint) instead of an in-process lock.
/// </summary>
public class BookingService(
    ISlotRepository slots,
    IBookingRepository bookings,
    IInstructorRepository instructors,
    IReviewRepository reviews,
    IClock clock,
    BookingOptions options)
{
    private readonly object _gate = new();

    public TimeSpan HoldDuration => TimeSpan.FromMinutes(options.HoldMinutes);

    public (decimal Fee, decimal InstructorAmount) CalculateSplit(decimal price)
    {
        var fee = Math.Round(price * options.PlatformFeePercent / 100m, 2, MidpointRounding.AwayFromZero);
        return (fee, price - fee);
    }

    public BookingResult HoldSlot(int studentId, int slotId)
    {
        lock (_gate)
        {
            var now = clock.Now;
            var slot = slots.GetById(slotId);
            if (slot is null) return BookingResult.Fail(BookingError.SlotNotFound);
            if (slot.Start <= now) return BookingResult.Fail(BookingError.SlotInPast);

            // Same student clicking again on a slot they already hold: reuse the pending booking.
            if (slot.Status == SlotStatus.Held && slot.HeldUntil > now && slot.BookingId is { } heldId &&
                bookings.GetById(heldId) is { } existing && existing.StudentId == studentId &&
                existing.Status == BookingStatus.PendingPayment)
            {
                return BookingResult.Ok(existing);
            }

            if (!slot.IsFree(now)) return BookingResult.Fail(BookingError.SlotUnavailable);

            ExpirePreviousHold(slot, now);

            var instructor = instructors.GetById(slot.InstructorId)!;
            var (fee, net) = CalculateSplit(instructor.PricePerLesson);
            var booking = bookings.Add(id => new Booking
            {
                Id = id, StudentId = studentId, InstructorId = instructor.Id, SlotId = slot.Id,
                Price = instructor.PricePerLesson, PlatformFee = fee, InstructorAmount = net,
                Status = BookingStatus.PendingPayment, CreatedAt = now
            });

            slot.Status = SlotStatus.Held;
            slot.HeldUntil = now + HoldDuration;
            slot.BookingId = booking.Id;
            return BookingResult.Ok(booking);
        }
    }

    public BookingResult ConfirmPayment(int bookingId, int studentId, PaymentMethod method, string? paymentLabel)
    {
        lock (_gate)
        {
            var now = clock.Now;
            var check = GetOwned(bookingId, studentId);
            if (!check.Success) return check;
            var booking = check.Booking!;

            if (booking.Status == BookingStatus.Confirmed) return BookingResult.Ok(booking); // idempotent
            if (booking.Status != BookingStatus.PendingPayment) return BookingResult.Fail(BookingError.InvalidState, booking);

            var slot = slots.GetById(booking.SlotId);
            if (slot is null || slot.Status != SlotStatus.Held || slot.BookingId != booking.Id || slot.HeldUntil <= now)
            {
                booking.Status = BookingStatus.Canceled;
                booking.CanceledAt = now;
                booking.CancelReason = "Tempo para pagamento expirou";
                if (slot is not null && slot.BookingId == booking.Id)
                {
                    slot.Status = SlotStatus.Available;
                    slot.HeldUntil = null;
                    slot.BookingId = null;
                }
                return BookingResult.Fail(BookingError.HoldExpired, booking);
            }

            slot.Status = SlotStatus.Booked;
            slot.HeldUntil = null;
            booking.Status = BookingStatus.Confirmed;
            booking.PaymentMethod = method;
            booking.PaymentLabel = paymentLabel;
            booking.PaidAt = now;
            return BookingResult.Ok(booking);
        }
    }

    public BookingResult Cancel(int bookingId, int studentId)
    {
        lock (_gate)
        {
            var now = clock.Now;
            var check = GetOwned(bookingId, studentId);
            if (!check.Success) return check;
            var booking = check.Booking!;

            var slot = slots.GetById(booking.SlotId);
            if (booking.Status is not (BookingStatus.PendingPayment or BookingStatus.Confirmed) ||
                (slot is not null && slot.Start <= now))
            {
                return BookingResult.Fail(BookingError.InvalidState, booking);
            }

            booking.Status = BookingStatus.Canceled;
            booking.CanceledAt = now;
            booking.CancelReason = "Cancelada pelo aluno";
            if (slot is not null && slot.BookingId == booking.Id)
            {
                slot.Status = SlotStatus.Available;
                slot.HeldUntil = null;
                slot.BookingId = null;
            }
            return BookingResult.Ok(booking);
        }
    }

    /// <summary>Returns the booking only if it belongs to the student, with its status refreshed.</summary>
    public BookingResult GetForStudent(int bookingId, int studentId)
    {
        lock (_gate)
        {
            var result = GetOwned(bookingId, studentId);
            if (result.Success) Refresh(result.Booking!, clock.Now);
            return result;
        }
    }

    public IReadOnlyList<Booking> ListForStudent(int studentId)
    {
        lock (_gate)
        {
            var now = clock.Now;
            var list = bookings.GetByStudent(studentId);
            foreach (var b in list) Refresh(b, now);
            return list;
        }
    }

    public bool HasReview(int bookingId) => reviews.ExistsForBooking(bookingId);

    public BookingResult AddReview(int bookingId, int studentId, string studentName, int rating, string? comment)
    {
        lock (_gate)
        {
            var now = clock.Now;
            if (rating is < 1 or > 5) return BookingResult.Fail(BookingError.InvalidRating);
            var check = GetOwned(bookingId, studentId);
            if (!check.Success) return check;
            var booking = check.Booking!;
            Refresh(booking, now);

            if (booking.Status != BookingStatus.Completed) return BookingResult.Fail(BookingError.NotCompleted, booking);
            if (reviews.ExistsForBooking(booking.Id)) return BookingResult.Fail(BookingError.AlreadyReviewed, booking);

            reviews.Add(id => new Review
            {
                Id = id, InstructorId = booking.InstructorId, StudentId = studentId, StudentName = studentName,
                BookingId = booking.Id, Rating = rating, Comment = (comment ?? "").Trim(), CreatedAt = now
            });

            var instructor = instructors.GetById(booking.InstructorId)!;
            var total = instructor.AverageRating * instructor.ReviewCount + rating;
            instructor.ReviewCount++;
            instructor.AverageRating = Math.Round(total / instructor.ReviewCount, 2);
            return BookingResult.Ok(booking);
        }
    }

    /// <summary>Slots the student can pick right now (free and in the future).</summary>
    public IReadOnlyList<Slot> GetBookableSlots(int instructorId, DateTime until)
    {
        var now = clock.Now;
        return slots.GetByInstructor(instructorId)
            .Where(s => s.Start > now && s.Start < until && s.IsFree(now))
            .ToList();
    }

    private BookingResult GetOwned(int bookingId, int studentId)
    {
        var booking = bookings.GetById(bookingId);
        if (booking is null) return BookingResult.Fail(BookingError.BookingNotFound);
        // Same response as "not found" is returned by controllers to avoid leaking other users' bookings.
        if (booking.StudentId != studentId) return BookingResult.Fail(BookingError.NotOwner);
        return BookingResult.Ok(booking);
    }

    private void ExpirePreviousHold(Slot slot, DateTime now)
    {
        if (slot.Status == SlotStatus.Held && slot.BookingId is { } oldId && bookings.GetById(oldId) is { } old &&
            old.Status == BookingStatus.PendingPayment)
        {
            old.Status = BookingStatus.Canceled;
            old.CanceledAt = now;
            old.CancelReason = "Tempo para pagamento expirou";
        }
    }

    private void Refresh(Booking booking, DateTime now)
    {
        var slot = slots.GetById(booking.SlotId);
        if (slot is null) return;

        if (booking.Status == BookingStatus.Confirmed && slot.End <= now)
        {
            booking.Status = BookingStatus.Completed;
            var instructor = instructors.GetById(booking.InstructorId);
            if (instructor is not null) instructor.LessonsGiven++;
        }
        else if (booking.Status == BookingStatus.PendingPayment &&
                 (slot.BookingId != booking.Id || slot.HeldUntil <= now))
        {
            booking.Status = BookingStatus.Canceled;
            booking.CanceledAt = now;
            booking.CancelReason = "Tempo para pagamento expirou";
            if (slot.BookingId == booking.Id)
            {
                slot.Status = SlotStatus.Available;
                slot.HeldUntil = null;
                slot.BookingId = null;
            }
        }
    }
}
