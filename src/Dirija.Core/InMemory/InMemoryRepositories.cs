using System.Collections.Concurrent;
using Dirija.Core.Abstractions;
using Dirija.Core.Entities;

namespace Dirija.Core.InMemory;

/// <summary>
/// Shared in-memory state for the demo. Data is lost on restart.
/// Will be replaced by EF Core + PostgreSQL repositories implementing the same interfaces.
/// </summary>
public sealed class InMemoryStore
{
    public ConcurrentDictionary<int, User> Users { get; } = new();
    public ConcurrentDictionary<int, Instructor> Instructors { get; } = new();
    public ConcurrentDictionary<int, Slot> Slots { get; } = new();
    public ConcurrentDictionary<int, Booking> Bookings { get; } = new();
    public ConcurrentDictionary<int, Review> Reviews { get; } = new();

    private int _bookingSeq;
    private int _reviewSeq;

    public int NextBookingId() => Interlocked.Increment(ref _bookingSeq);
    public int NextReviewId() => Interlocked.Increment(ref _reviewSeq);

    internal void SyncSequences()
    {
        _bookingSeq = Bookings.Keys.DefaultIfEmpty(0).Max();
        _reviewSeq = Reviews.Keys.DefaultIfEmpty(0).Max();
    }
}

public sealed class InMemoryUserRepository(InMemoryStore store) : IUserRepository
{
    public User? GetById(int id) => store.Users.GetValueOrDefault(id);

    public User? ValidateCredentials(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password)) return null;
        var user = store.Users.Values.FirstOrDefault(u =>
            string.Equals(u.Email, email.Trim(), StringComparison.OrdinalIgnoreCase));
        return user is not null && user.Password == password ? user : null;
    }
}

public sealed class InMemoryInstructorRepository(InMemoryStore store) : IInstructorRepository
{
    public IReadOnlyList<Instructor> GetAll() => store.Instructors.Values.OrderBy(i => i.Id).ToList();
    public Instructor? GetById(int id) => store.Instructors.GetValueOrDefault(id);
}

public sealed class InMemorySlotRepository(InMemoryStore store) : ISlotRepository
{
    public Slot? GetById(int id) => store.Slots.GetValueOrDefault(id);

    public IReadOnlyList<Slot> GetByInstructor(int instructorId) =>
        store.Slots.Values.Where(s => s.InstructorId == instructorId).OrderBy(s => s.Start).ToList();
}

public sealed class InMemoryBookingRepository(InMemoryStore store) : IBookingRepository
{
    public Booking Add(Func<int, Booking> factory)
    {
        var booking = factory(store.NextBookingId());
        store.Bookings[booking.Id] = booking;
        return booking;
    }

    public Booking? GetById(int id) => store.Bookings.GetValueOrDefault(id);

    public IReadOnlyList<Booking> GetByStudent(int studentId) =>
        store.Bookings.Values.Where(b => b.StudentId == studentId).ToList();
}

public sealed class InMemoryReviewRepository(InMemoryStore store) : IReviewRepository
{
    public Review Add(Func<int, Review> factory)
    {
        var review = factory(store.NextReviewId());
        store.Reviews[review.Id] = review;
        return review;
    }

    public IReadOnlyList<Review> GetByInstructor(int instructorId) =>
        store.Reviews.Values.Where(r => r.InstructorId == instructorId)
            .OrderByDescending(r => r.CreatedAt).ToList();

    public bool ExistsForBooking(int bookingId) =>
        store.Reviews.Values.Any(r => r.BookingId == bookingId);
}
