using Dirija.Core.Entities;

namespace Dirija.Core.Abstractions;

public interface IClock
{
    DateTime Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}

public interface IUserRepository
{
    User? GetById(int id);
    /// <summary>Returns the user when the e-mail/password pair matches, otherwise null.</summary>
    User? ValidateCredentials(string email, string password);
}

public interface IInstructorRepository
{
    IReadOnlyList<Instructor> GetAll();
    Instructor? GetById(int id);
}

public interface ISlotRepository
{
    Slot? GetById(int id);
    IReadOnlyList<Slot> GetByInstructor(int instructorId);
}

public interface IBookingRepository
{
    Booking Add(Func<int, Booking> factory);
    Booking? GetById(int id);
    IReadOnlyList<Booking> GetByStudent(int studentId);
}

public interface IReviewRepository
{
    Review Add(Func<int, Review> factory);
    IReadOnlyList<Review> GetByInstructor(int instructorId);
    bool ExistsForBooking(int bookingId);
}
