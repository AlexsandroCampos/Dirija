using Dirija.Core.Entities;

namespace Dirija.Core.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>Thrown when an optimistic concurrency check fails (someone else changed the same row first).</summary>
public sealed class ConcurrencyConflictException(string message, Exception? inner = null) : Exception(message, inner);

public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}

public interface IUnitOfWork
{
    /// <exception cref="ConcurrencyConflictException">When a versioned row was changed concurrently.</exception>
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default);
    /// <summary>Forget pending (not saved) changes, e.g. after a concurrency conflict.</summary>
    void DiscardChanges();
}

public interface IUserRepository
{
    Task<User?> GetByIdAsync(int id);
    Task<User?> GetByEmailAsync(string email);
}

public interface IAuthService
{
    /// <summary>Returns the user when the e-mail/password pair matches, otherwise null.</summary>
    Task<User?> ValidateCredentialsAsync(string email, string password);
}

/// <summary>Coarse database-side filter: bounding box around the origin plus simple attribute filters.</summary>
public record InstructorCandidateFilter(
    double MinLatitude, double MaxLatitude, double MinLongitude, double MaxLongitude,
    LicenseCategory? Category, double? MinRating);

public interface IInstructorRepository
{
    Task<Instructor?> GetByIdAsync(int id);
    Task<IReadOnlyList<Instructor>> GetByIdsAsync(IEnumerable<int> ids);
    /// <summary>Verified instructors inside the bounding box that match the filters.</summary>
    Task<IReadOnlyList<Instructor>> FindVerifiedCandidatesAsync(InstructorCandidateFilter filter);
}

public interface ISlotRepository
{
    Task<Slot?> GetByIdAsync(int id);
    Task<IReadOnlyList<Slot>> GetByIdsAsync(IEnumerable<int> ids);
    /// <summary>Slots starting in (nowUtc, untilUtc) that are available or whose hold already expired.</summary>
    Task<IReadOnlyList<Slot>> GetFreeAsync(int instructorId, DateTime nowUtc, DateTime untilUtc);
}

public interface IBookingRepository
{
    void Add(Booking booking);
    Task<Booking?> GetByIdAsync(int id);
    Task<IReadOnlyList<Booking>> GetByStudentAsync(int studentId);
}

public interface IReviewRepository
{
    void Add(Review review);
    Task<IReadOnlyList<Review>> GetByInstructorAsync(int instructorId);
    Task<bool> ExistsForBookingAsync(int bookingId);
    Task<IReadOnlySet<int>> GetReviewedBookingIdsAsync(IEnumerable<int> bookingIds);
}
