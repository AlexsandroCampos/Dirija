using Dirija.Core.Abstractions;
using Dirija.Core.Entities;

namespace Dirija.Core.Services;

public class BookingOptions
{
    public const string SectionName = "Booking";
    /// <summary>Platform take rate in percent (spec does not define it; 15% for the MVP).</summary>
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
/// Reservation workflow. Anti-overbooking relies on optimistic concurrency: every write to a slot checks its
/// <see cref="Slot.Version"/>; if two students race for the same slot, only the first commit succeeds and the
/// other gets <see cref="BookingError.SlotUnavailable"/>. All times are UTC.
/// </summary>
public class BookingService(
    ISlotRepository slots,
    IBookingRepository bookings,
    IInstructorRepository instructors,
    IReviewRepository reviews,
    IUnitOfWork unitOfWork,
    IClock clock,
    BookingOptions options)
{
    private const string ExpiredReason = "Tempo para pagamento expirou";

    public TimeSpan HoldDuration => TimeSpan.FromMinutes(options.HoldMinutes);

    public (decimal Fee, decimal InstructorAmount) CalculateSplit(decimal price)
    {
        var fee = Math.Round(price * options.PlatformFeePercent / 100m, 2, MidpointRounding.AwayFromZero);
        return (fee, price - fee);
    }

    public async Task<BookingResult> HoldSlotAsync(int studentId, int slotId)
    {
        var now = clock.UtcNow;
        var slot = await slots.GetByIdAsync(slotId);
        if (slot is null) return BookingResult.Fail(BookingError.SlotNotFound);
        if (slot.Start <= now) return BookingResult.Fail(BookingError.SlotInPast);

        // Same student clicking again on a slot they already hold: reuse the pending booking.
        if (slot.Status == SlotStatus.Held && slot.HeldUntil > now && slot.BookingId is { } heldId &&
            await bookings.GetByIdAsync(heldId) is { Status: BookingStatus.PendingPayment } existing &&
            existing.StudentId == studentId)
        {
            return BookingResult.Ok(existing);
        }

        if (!slot.IsFree(now)) return BookingResult.Fail(BookingError.SlotUnavailable);

        var instructor = await instructors.GetByIdAsync(slot.InstructorId);
        if (instructor is null) return BookingResult.Fail(BookingError.SlotNotFound);
        var (fee, net) = CalculateSplit(instructor.PricePerLesson);

        try
        {
            await using var tx = await unitOfWork.BeginTransactionAsync();
            await ExpirePreviousHoldAsync(slot, now);

            var booking = new Booking
            {
                StudentId = studentId, InstructorId = instructor.Id, SlotId = slot.Id,
                Price = instructor.PricePerLesson, PlatformFee = fee, InstructorAmount = net,
                Status = BookingStatus.PendingPayment, CreatedAt = now
            };
            bookings.Add(booking);
            await unitOfWork.SaveChangesAsync(); // assigns booking.Id

            slot.Status = SlotStatus.Held;
            slot.HeldUntil = now + HoldDuration;
            slot.BookingId = booking.Id;
            await unitOfWork.SaveChangesAsync(); // fails here if another request took the slot

            await tx.CommitAsync();
            return BookingResult.Ok(booking);
        }
        catch (ConcurrencyConflictException)
        {
            unitOfWork.DiscardChanges();
            return BookingResult.Fail(BookingError.SlotUnavailable);
        }
    }

    public async Task<BookingResult> ConfirmPaymentAsync(int bookingId, int studentId, PaymentMethod method, string? paymentLabel)
    {
        var now = clock.UtcNow;
        var check = await GetOwnedAsync(bookingId, studentId);
        if (!check.Success) return check;
        var booking = check.Booking!;

        if (booking.Status == BookingStatus.Confirmed) return BookingResult.Ok(booking); // idempotent
        if (booking.Status != BookingStatus.PendingPayment) return BookingResult.Fail(BookingError.InvalidState, booking);

        var slot = await slots.GetByIdAsync(booking.SlotId);
        if (slot is null || slot.Status != SlotStatus.Held || slot.BookingId != booking.Id || slot.HeldUntil <= now)
        {
            CancelBooking(booking, now, ExpiredReason);
            if (slot is not null && slot.BookingId == booking.Id) Release(slot);
            await TrySaveAsync();
            return BookingResult.Fail(BookingError.HoldExpired, booking);
        }

        slot.Status = SlotStatus.Booked;
        slot.HeldUntil = null;
        booking.Status = BookingStatus.Confirmed;
        booking.PaymentMethod = method;
        booking.PaymentLabel = paymentLabel;
        booking.PaidAt = now;

        return await TrySaveAsync()
            ? BookingResult.Ok(booking)
            : BookingResult.Fail(BookingError.HoldExpired, booking);
    }

    public async Task<BookingResult> CancelAsync(int bookingId, int studentId)
    {
        var now = clock.UtcNow;
        var check = await GetOwnedAsync(bookingId, studentId);
        if (!check.Success) return check;
        var booking = check.Booking!;

        var slot = await slots.GetByIdAsync(booking.SlotId);
        if (booking.Status is not (BookingStatus.PendingPayment or BookingStatus.Confirmed) ||
            (slot is not null && slot.Start <= now))
        {
            return BookingResult.Fail(BookingError.InvalidState, booking);
        }

        CancelBooking(booking, now, "Cancelada pelo aluno");
        if (slot is not null && slot.BookingId == booking.Id) Release(slot);

        return await TrySaveAsync()
            ? BookingResult.Ok(booking)
            : BookingResult.Fail(BookingError.InvalidState, booking);
    }

    /// <summary>Returns the booking only if it belongs to the student, with its status refreshed.</summary>
    public async Task<BookingResult> GetForStudentAsync(int bookingId, int studentId)
    {
        var result = await GetOwnedAsync(bookingId, studentId);
        if (!result.Success) return result;

        var slot = await slots.GetByIdAsync(result.Booking!.SlotId);
        if (slot is not null && await RefreshAsync(result.Booking, slot, clock.UtcNow)) await TrySaveAsync();
        return result;
    }

    public async Task<IReadOnlyList<Booking>> ListForStudentAsync(int studentId)
    {
        var now = clock.UtcNow;
        var list = await bookings.GetByStudentAsync(studentId);
        var slotMap = (await slots.GetByIdsAsync(list.Select(b => b.SlotId))).ToDictionary(s => s.Id);

        var changed = false;
        foreach (var b in list)
        {
            if (slotMap.TryGetValue(b.SlotId, out var slot)) changed |= await RefreshAsync(b, slot, now);
        }
        if (changed) await TrySaveAsync();
        return list;
    }

    public Task<bool> HasReviewAsync(int bookingId) => reviews.ExistsForBookingAsync(bookingId);

    public Task<IReadOnlySet<int>> GetReviewedBookingIdsAsync(IEnumerable<int> bookingIds) =>
        reviews.GetReviewedBookingIdsAsync(bookingIds);

    public async Task<BookingResult> AddReviewAsync(int bookingId, int studentId, string studentName, int rating, string? comment)
    {
        if (rating is < 1 or > 5) return BookingResult.Fail(BookingError.InvalidRating);
        var check = await GetForStudentAsync(bookingId, studentId);
        if (!check.Success) return check;
        var booking = check.Booking!;

        if (booking.Status != BookingStatus.Completed) return BookingResult.Fail(BookingError.NotCompleted, booking);
        if (await reviews.ExistsForBookingAsync(booking.Id)) return BookingResult.Fail(BookingError.AlreadyReviewed, booking);

        reviews.Add(new Review
        {
            InstructorId = booking.InstructorId, StudentId = studentId, StudentName = studentName,
            BookingId = booking.Id, Rating = rating, Comment = (comment ?? "").Trim(), CreatedAt = clock.UtcNow
        });

        // Incremental average: seed instructors carry historical counts that are not all stored as rows.
        var instructor = (await instructors.GetByIdAsync(booking.InstructorId))!;
        var total = instructor.AverageRating * instructor.ReviewCount + rating;
        instructor.ReviewCount++;
        instructor.AverageRating = Math.Round(total / instructor.ReviewCount, 2);

        await unitOfWork.SaveChangesAsync();
        return BookingResult.Ok(booking);
    }

    /// <summary>Slots the student can pick right now (free and in the future, until the given UTC instant).</summary>
    public Task<IReadOnlyList<Slot>> GetBookableSlotsAsync(int instructorId, DateTime untilUtc) =>
        slots.GetFreeAsync(instructorId, clock.UtcNow, untilUtc);

    private async Task<BookingResult> GetOwnedAsync(int bookingId, int studentId)
    {
        var booking = await bookings.GetByIdAsync(bookingId);
        if (booking is null) return BookingResult.Fail(BookingError.BookingNotFound);
        // Controllers answer "not found" in both cases to avoid leaking other users' bookings.
        if (booking.StudentId != studentId) return BookingResult.Fail(BookingError.NotOwner);
        return BookingResult.Ok(booking);
    }

    private async Task ExpirePreviousHoldAsync(Slot slot, DateTime now)
    {
        if (slot.Status == SlotStatus.Held && slot.BookingId is { } oldId &&
            await bookings.GetByIdAsync(oldId) is { Status: BookingStatus.PendingPayment } old)
        {
            CancelBooking(old, now, ExpiredReason);
        }
    }

    /// <summary>Applies time-based transitions. Returns true when something changed and must be saved.</summary>
    private async Task<bool> RefreshAsync(Booking booking, Slot slot, DateTime now)
    {
        if (booking.Status == BookingStatus.Confirmed && slot.End <= now)
        {
            booking.Status = BookingStatus.Completed;
            if (await instructors.GetByIdAsync(booking.InstructorId) is { } instructor) instructor.LessonsGiven++;
            return true;
        }

        if (booking.Status == BookingStatus.PendingPayment && (slot.BookingId != booking.Id || slot.HeldUntil <= now))
        {
            CancelBooking(booking, now, ExpiredReason);
            if (slot.BookingId == booking.Id) Release(slot);
            return true;
        }

        return false;
    }

    private static void CancelBooking(Booking booking, DateTime now, string reason)
    {
        booking.Status = BookingStatus.Canceled;
        booking.CanceledAt = now;
        booking.CancelReason = reason;
    }

    private static void Release(Slot slot)
    {
        slot.Status = SlotStatus.Available;
        slot.HeldUntil = null;
        slot.BookingId = null;
    }

    /// <summary>Saves; on a concurrency conflict discards local changes and returns false.</summary>
    private async Task<bool> TrySaveAsync()
    {
        try
        {
            await unitOfWork.SaveChangesAsync();
            return true;
        }
        catch (ConcurrencyConflictException)
        {
            unitOfWork.DiscardChanges();
            return false;
        }
    }
}
