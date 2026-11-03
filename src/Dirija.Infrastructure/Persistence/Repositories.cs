using Dirija.Core.Abstractions;
using Dirija.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Dirija.Infrastructure.Persistence;

public sealed class EfUnitOfWork(DirijaDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("O registro foi alterado por outra operação.", ex);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new UniqueConstraintException("Registro duplicado.", ex);
        }
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
        new Transaction(await db.Database.BeginTransactionAsync(ct));

    public void DiscardChanges() => db.ChangeTracker.Clear();

    /// <summary>
    /// SQLite: "UNIQUE constraint failed"; PostgreSQL: "duplicate key value violates unique constraint".
    /// Matching the message keeps this provider-neutral.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Disposing without <see cref="CommitAsync"/> rolls the transaction back.</summary>
    private sealed class Transaction(IDbContextTransaction inner) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => inner.CommitAsync(ct);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

public sealed class EfUserRepository(DirijaDbContext db) : IUserRepository
{
    public void Add(User user) => db.Users.Add(user);

    public Task<User?> GetByIdAsync(int id) => db.Users.FirstOrDefaultAsync(u => u.Id == id);

    public async Task<IReadOnlyList<User>> GetByIdsAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        return await db.Users.Where(u => list.Contains(u.Id)).ToListAsync();
    }

    public Task<User?> GetByEmailAsync(string email)
    {
        var normalized = (email ?? "").Trim().ToLowerInvariant();
        return db.Users.FirstOrDefaultAsync(u => u.Email == normalized);
    }
}

public sealed class EfInstructorRepository(DirijaDbContext db) : IInstructorRepository
{
    public void Add(Instructor instructor) => db.Instructors.Add(instructor);

    public Task<Instructor?> GetByIdAsync(int id) => db.Instructors.FirstOrDefaultAsync(i => i.Id == id);

    public Task<Instructor?> GetByUserIdAsync(int userId) => db.Instructors.FirstOrDefaultAsync(i => i.UserId == userId);

    public async Task<IReadOnlyList<Instructor>> GetByIdsAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        return await db.Instructors.Where(i => list.Contains(i.Id)).ToListAsync();
    }

    public async Task<IReadOnlyList<Instructor>> FindApprovedCandidatesAsync(InstructorCandidateFilter f)
    {
        var q = db.Instructors.Where(i => i.Status == InstructorStatus.Approved &&
                                          i.Latitude >= f.MinLatitude && i.Latitude <= f.MaxLatitude &&
                                          i.Longitude >= f.MinLongitude && i.Longitude <= f.MaxLongitude);
        if (f.Category is { } cat) q = q.Where(i => (i.Categories & cat) == cat);
        if (f.MinRating is { } min) q = q.Where(i => i.AverageRating >= min);
        return await q.ToListAsync();
    }

    public async Task<IReadOnlyList<Instructor>> ListByStatusAsync(InstructorStatus status) =>
        await db.Instructors.Where(i => i.Status == status && i.UserId != null)
            .OrderBy(i => i.SubmittedAt).ThenBy(i => i.Id).ToListAsync();

    public async Task<IReadOnlyDictionary<InstructorStatus, int>> CountByStatusAsync()
    {
        var counts = await db.Instructors.Where(i => i.UserId != null)
            .GroupBy(i => i.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        return Enum.GetValues<InstructorStatus>()
            .ToDictionary(s => s, s => counts.FirstOrDefault(c => c.Key == s)?.Count ?? 0);
    }

    public async Task<IReadOnlyList<int>> GetIdsWithRealAgendaAsync() =>
        await db.Instructors.Where(i => !i.IsDemo).Select(i => i.Id).ToListAsync();
}

public sealed class EfSlotRepository(DirijaDbContext db) : ISlotRepository
{
    public void Add(Slot slot) => db.Slots.Add(slot);

    public Task<Slot?> GetByIdAsync(int id) => db.Slots.FirstOrDefaultAsync(s => s.Id == id);

    public async Task<IReadOnlyList<Slot>> GetByIdsAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        return await db.Slots.Where(s => list.Contains(s.Id)).ToListAsync();
    }

    public async Task<IReadOnlyList<Slot>> GetFreeAsync(int instructorId, DateTime nowUtc, DateTime fromUtc, DateTime untilUtc)
    {
        return await db.Slots
            .Where(s => s.InstructorId == instructorId && s.IsActive && s.Start > fromUtc && s.Start < untilUtc &&
                        (s.Status == SlotStatus.Available ||
                         (s.Status == SlotStatus.Held && s.HeldUntil != null && s.HeldUntil <= nowUtc)) &&
                        !db.TimeOffs.Any(t => t.InstructorId == s.InstructorId && t.Start < s.End && t.End > s.Start))
            .OrderBy(s => s.Start)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Slot>> GetRangeAsync(int instructorId, DateTime fromUtc, DateTime untilUtc) =>
        await db.Slots
            .Where(s => s.InstructorId == instructorId && s.Start >= fromUtc && s.Start < untilUtc)
            .OrderBy(s => s.Start)
            .ToListAsync();
}

public sealed class EfAgendaRepository(DirijaDbContext db) : IAgendaRepository
{
    public async Task<IReadOnlyList<WeeklyAvailability>> GetRulesAsync(int instructorId) =>
        await db.WeeklyAvailability.Where(r => r.InstructorId == instructorId)
            .OrderBy(r => r.DayOfWeek).ThenBy(r => r.StartMinute).ToListAsync();

    public void RemoveRules(IEnumerable<WeeklyAvailability> rules) => db.WeeklyAvailability.RemoveRange(rules);

    public void AddRule(WeeklyAvailability rule) => db.WeeklyAvailability.Add(rule);

    public void AddTimeOff(TimeOff timeOff) => db.TimeOffs.Add(timeOff);

    public void RemoveTimeOff(TimeOff timeOff) => db.TimeOffs.Remove(timeOff);

    public Task<TimeOff?> GetTimeOffAsync(int id) => db.TimeOffs.FirstOrDefaultAsync(t => t.Id == id);

    public async Task<IReadOnlyList<TimeOff>> GetTimeOffsAsync(int instructorId, DateTime fromUtc) =>
        await db.TimeOffs.Where(t => t.InstructorId == instructorId && t.End > fromUtc)
            .OrderBy(t => t.Start).ToListAsync();

    public async Task<IReadOnlyList<TimeOff>> GetOverlappingTimeOffsAsync(int instructorId, DateTime startUtc, DateTime endUtc) =>
        await db.TimeOffs.Where(t => t.InstructorId == instructorId && t.Start < endUtc && t.End > startUtc)
            .OrderBy(t => t.Start).ToListAsync();
}

public sealed class EfBookingRepository(DirijaDbContext db) : IBookingRepository
{
    public void Add(Booking booking) => db.Bookings.Add(booking);

    public Task<Booking?> GetByIdAsync(int id) => db.Bookings.FirstOrDefaultAsync(b => b.Id == id);

    public async Task<IReadOnlyList<Booking>> GetByIdsAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        return await db.Bookings.Where(b => list.Contains(b.Id)).ToListAsync();
    }

    public async Task<IReadOnlyList<Booking>> GetByStudentAsync(int studentId) =>
        await db.Bookings.Where(b => b.StudentId == studentId).ToListAsync();

    public async Task<IReadOnlyList<Booking>> GetByInstructorAsync(int instructorId) =>
        await db.Bookings.Where(b => b.InstructorId == instructorId).ToListAsync();
}

public sealed class EfReviewRepository(DirijaDbContext db) : IReviewRepository
{
    public void Add(Review review) => db.Reviews.Add(review);

    public async Task<IReadOnlyList<Review>> GetByInstructorAsync(int instructorId) =>
        await db.Reviews.Where(r => r.InstructorId == instructorId)
            .OrderByDescending(r => r.CreatedAt).ToListAsync();

    public Task<bool> ExistsForBookingAsync(int bookingId) =>
        db.Reviews.AnyAsync(r => r.BookingId == bookingId);

    public async Task<IReadOnlySet<int>> GetReviewedBookingIdsAsync(IEnumerable<int> bookingIds)
    {
        var list = bookingIds.Distinct().Select(id => (int?)id).ToList();
        var found = await db.Reviews.Where(r => list.Contains(r.BookingId)).Select(r => r.BookingId!.Value).ToListAsync();
        return found.ToHashSet();
    }
}
