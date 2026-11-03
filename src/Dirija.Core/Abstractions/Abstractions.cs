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

/// <summary>Thrown when a unique index rejects the write (e.g. e-mail already registered, duplicated slot).</summary>
public sealed class UniqueConstraintException(string message, Exception? inner = null) : Exception(message, inner);

public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}

public interface IUnitOfWork
{
    /// <exception cref="ConcurrencyConflictException">When a versioned row was changed concurrently.</exception>
    /// <exception cref="UniqueConstraintException">When a unique index rejects the write.</exception>
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default);
    /// <summary>Forget pending (not saved) changes, e.g. after a concurrency conflict.</summary>
    void DiscardChanges();
}

/// <summary>Password hashing (PBKDF2), kept behind an interface so the Core does not depend on ASP.NET Identity.</summary>
public interface IPasswordService
{
    string Hash(User user, string password);
    bool Verify(User user, string password);
}

public interface IUserRepository
{
    void Add(User user);
    Task<User?> GetByIdAsync(int id);
    Task<IReadOnlyList<User>> GetByIdsAsync(IEnumerable<int> ids);
    Task<User?> GetByEmailAsync(string email);
}

public interface IAuthService
{
    /// <summary>
    /// Returns the user when the e-mail/password pair matches, otherwise null. Five wrong passwords lock the
    /// account for 15 minutes; a locked account also returns null (callers always show the same generic message).
    /// </summary>
    Task<User?> ValidateCredentialsAsync(string email, string password);
}

/// <summary>Coarse database-side filter: bounding box around the origin plus simple attribute filters.</summary>
public record InstructorCandidateFilter(
    double MinLatitude, double MaxLatitude, double MinLongitude, double MaxLongitude,
    LicenseCategory? Category, double? MinRating);

public interface IInstructorRepository
{
    void Add(Instructor instructor);
    Task<Instructor?> GetByIdAsync(int id);
    Task<Instructor?> GetByUserIdAsync(int userId);
    Task<IReadOnlyList<Instructor>> GetByIdsAsync(IEnumerable<int> ids);
    /// <summary>Approved instructors inside the bounding box that match the filters.</summary>
    Task<IReadOnlyList<Instructor>> FindApprovedCandidatesAsync(InstructorCandidateFilter filter);
    Task<IReadOnlyList<Instructor>> ListByStatusAsync(InstructorStatus status);
    Task<IReadOnlyDictionary<InstructorStatus, int>> CountByStatusAsync();
    /// <summary>Instructors whose agenda comes from a weekly availability (not the fictional demo ones).</summary>
    Task<IReadOnlyList<int>> GetIdsWithRealAgendaAsync();
}

public interface ISlotRepository
{
    void Add(Slot slot);
    Task<Slot?> GetByIdAsync(int id);
    Task<IReadOnlyList<Slot>> GetByIdsAsync(IEnumerable<int> ids);
    /// <summary>
    /// Active slots starting in (fromUtc, untilUtc) that are available (or whose hold expired before nowUtc) and
    /// that are not covered by a time off.
    /// </summary>
    Task<IReadOnlyList<Slot>> GetFreeAsync(int instructorId, DateTime nowUtc, DateTime fromUtc, DateTime untilUtc);
    /// <summary>Every slot (active or not, any status) starting in [fromUtc, untilUtc).</summary>
    Task<IReadOnlyList<Slot>> GetRangeAsync(int instructorId, DateTime fromUtc, DateTime untilUtc);
}

/// <summary>Weekly availability rules and time offs of an instructor.</summary>
public interface IAgendaRepository
{
    Task<IReadOnlyList<WeeklyAvailability>> GetRulesAsync(int instructorId);
    void RemoveRules(IEnumerable<WeeklyAvailability> rules);
    void AddRule(WeeklyAvailability rule);
    void AddTimeOff(TimeOff timeOff);
    void RemoveTimeOff(TimeOff timeOff);
    Task<TimeOff?> GetTimeOffAsync(int id);
    /// <summary>Time offs that end after <paramref name="fromUtc"/>, ordered by start.</summary>
    Task<IReadOnlyList<TimeOff>> GetTimeOffsAsync(int instructorId, DateTime fromUtc);
    Task<IReadOnlyList<TimeOff>> GetOverlappingTimeOffsAsync(int instructorId, DateTime startUtc, DateTime endUtc);
}

public interface IBookingRepository
{
    void Add(Booking booking);
    Task<Booking?> GetByIdAsync(int id);
    Task<IReadOnlyList<Booking>> GetByIdsAsync(IEnumerable<int> ids);
    Task<IReadOnlyList<Booking>> GetByStudentAsync(int studentId);
    Task<IReadOnlyList<Booking>> GetByInstructorAsync(int instructorId);
}

public interface IReviewRepository
{
    void Add(Review review);
    Task<IReadOnlyList<Review>> GetByInstructorAsync(int instructorId);
    Task<bool> ExistsForBookingAsync(int bookingId);
    Task<IReadOnlySet<int>> GetReviewedBookingIdsAsync(IEnumerable<int> bookingIds);
}
