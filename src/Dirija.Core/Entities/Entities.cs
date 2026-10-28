namespace Dirija.Core.Entities;

public enum UserRole
{
    Student,
    Instructor,
    Admin
}

/// <summary>CNH categories served by an instructor.</summary>
[Flags]
public enum LicenseCategory
{
    None = 0,
    A = 1,
    B = 2
}

public enum SlotStatus
{
    Available,
    /// <summary>Temporarily reserved while the student completes checkout.</summary>
    Held,
    Booked,
    /// <summary>Blocked by the instructor (unavailable).</summary>
    Blocked
}

public enum BookingStatus
{
    PendingPayment,
    Confirmed,
    Completed,
    Canceled
}

public enum PaymentMethod
{
    None,
    Pix,
    CreditCard
}

/// <summary>
/// Optimistic concurrency token. The persistence layer increments <see cref="Version"/> on every update and
/// rejects the write if another request changed the row first (this is what prevents double booking).
/// </summary>
public interface IVersioned
{
    int Version { get; set; }
}

public class User
{
    public int Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Always stored lower-case so lookups are case-insensitive on every database.</summary>
    public required string Email { get; init; }
    public string Phone { get; init; } = "";
    /// <summary>ASP.NET Core Identity password hash (PBKDF2). Never the plain password.</summary>
    public required string PasswordHash { get; set; }
    public UserRole Role { get; init; } = UserRole.Student;
    public string Neighborhood { get; init; } = "";
}

public class Instructor
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string? PhotoUrl { get; init; }
    public string Bio { get; init; } = "";
    public LicenseCategory Categories { get; init; }
    public decimal PricePerLesson { get; init; }
    public required string Neighborhood { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double ServiceRadiusKm { get; init; }
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public int LessonsGiven { get; set; }
    public string Vehicle { get; init; } = "";
    public bool HasDualControl { get; init; }
    public bool IsVerified { get; init; }
    public string DetranCredential { get; init; } = "";
    public string MeetingPoint { get; init; } = "";
    public int LessonMinutes { get; init; } = 50;

    public string Initials =>
        string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => p[0]));
}

/// <summary>A lesson time window. All dates are UTC.</summary>
public class Slot : IVersioned
{
    public int Id { get; init; }
    public int InstructorId { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    public SlotStatus Status { get; set; } = SlotStatus.Available;
    public DateTime? HeldUntil { get; set; }
    public int? BookingId { get; set; }
    public int Version { get; set; }

    /// <summary>A slot is free when available, or when a previous hold has already expired.</summary>
    public bool IsFree(DateTime nowUtc) =>
        Status == SlotStatus.Available ||
        (Status == SlotStatus.Held && HeldUntil is { } until && until <= nowUtc);
}

/// <summary>A student's reservation. All dates are UTC.</summary>
public class Booking : IVersioned
{
    public int Id { get; init; }
    public int StudentId { get; init; }
    public int InstructorId { get; init; }
    public int SlotId { get; init; }
    public decimal Price { get; init; }
    public decimal PlatformFee { get; init; }
    public decimal InstructorAmount { get; init; }
    public BookingStatus Status { get; set; } = BookingStatus.PendingPayment;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.None;
    /// <summary>Human readable label such as "Cartão final 1234". Never stores the full card number.</summary>
    public string? PaymentLabel { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime? PaidAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public string? CancelReason { get; set; }
    public int Version { get; set; }
}

public class Review
{
    public int Id { get; init; }
    public int InstructorId { get; init; }
    public int StudentId { get; init; }
    public required string StudentName { get; init; }
    public int? BookingId { get; init; }
    public int Rating { get; init; }
    public string Comment { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}
