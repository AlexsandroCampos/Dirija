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
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
        new Transaction(await db.Database.BeginTransactionAsync(ct));

    public void DiscardChanges() => db.ChangeTracker.Clear();

    /// <summary>Disposing without <see cref="CommitAsync"/> rolls the transaction back.</summary>
    private sealed class Transaction(IDbContextTransaction inner) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => inner.CommitAsync(ct);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

public sealed class EfUserRepository(DirijaDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(int id) => db.Users.FirstOrDefaultAsync(u => u.Id == id);

    public Task<User?> GetByEmailAsync(string email)
    {
        var normalized = (email ?? "").Trim().ToLowerInvariant();
        return db.Users.FirstOrDefaultAsync(u => u.Email == normalized);
    }
}

public sealed class EfInstructorRepository(DirijaDbContext db) : IInstructorRepository
{
    public Task<Instructor?> GetByIdAsync(int id) => db.Instructors.FirstOrDefaultAsync(i => i.Id == id);

    public async Task<IReadOnlyList<Instructor>> GetByIdsAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        return await db.Instructors.Where(i => list.Contains(i.Id)).ToListAsync();
    }

    public async Task<IReadOnlyList<Instructor>> FindVerifiedCandidatesAsync(InstructorCandidateFilter f)
    {
        var q = db.Instructors.Where(i => i.IsVerified &&
                                          i.Latitude >= f.MinLatitude && i.Latitude <= f.MaxLatitude &&
                                          i.Longitude >= f.MinLongitude && i.Longitude <= f.MaxLongitude);
        if (f.Category is { } cat) q = q.Where(i => (i.Categories & cat) == cat);
        if (f.MinRating is { } min) q = q.Where(i => i.AverageRating >= min);
        return await q.ToListAsync();
    }
}

public sealed class EfSlotRepository(DirijaDbContext db) : ISlotRepository
{
    public Task<Slot?> GetByIdAsync(int id) => db.Slots.FirstOrDefaultAsync(s => s.Id == id);

    public async Task<IReadOnlyList<Slot>> GetByIdsAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        return await db.Slots.Where(s => list.Contains(s.Id)).ToListAsync();
    }

    public async Task<IReadOnlyList<Slot>> GetFreeAsync(int instructorId, DateTime nowUtc, DateTime untilUtc) =>
        await db.Slots
            .Where(s => s.InstructorId == instructorId && s.Start > nowUtc && s.Start < untilUtc &&
                        (s.Status == SlotStatus.Available ||
                         (s.Status == SlotStatus.Held && s.HeldUntil != null && s.HeldUntil <= nowUtc)))
            .OrderBy(s => s.Start)
            .ToListAsync();
}

public sealed class EfBookingRepository(DirijaDbContext db) : IBookingRepository
{
    public void Add(Booking booking) => db.Bookings.Add(booking);

    public Task<Booking?> GetByIdAsync(int id) => db.Bookings.FirstOrDefaultAsync(b => b.Id == id);

    public async Task<IReadOnlyList<Booking>> GetByStudentAsync(int studentId) =>
        await db.Bookings.Where(b => b.StudentId == studentId).ToListAsync();
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
