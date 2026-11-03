namespace Dirija.Core.Entities;

public enum UserRole
{
    Student,
    Instructor,
    Admin
}

/// <summary>Role names used in the auth cookie and in [Authorize(Roles = ...)].</summary>
public static class Roles
{
    public const string Student = nameof(UserRole.Student);
    public const string Instructor = nameof(UserRole.Instructor);
    public const string Admin = nameof(UserRole.Admin);
}

/// <summary>CNH categories served by an instructor.</summary>
[Flags]
public enum LicenseCategory
{
    None = 0,
    A = 1,
    B = 2
}

/// <summary>Review state of an instructor's registration. Only approved instructors appear in the search.</summary>
public enum InstructorStatus
{
    Pending,
    Approved,
    Rejected
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

public enum CanceledBy
{
    None,
    Student,
    Instructor,
    System
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
    public required string Name { get; set; }
    /// <summary>Always stored lower-case so lookups are case-insensitive on every database.</summary>
    public required string Email { get; init; }
    public string Phone { get; set; } = "";
    /// <summary>ASP.NET Core Identity password hash (PBKDF2). Never the plain password.</summary>
    public required string PasswordHash { get; set; }
    public UserRole Role { get; init; } = UserRole.Student;
    public string Neighborhood { get; set; } = "";
    public DateTime CreatedAt { get; init; }
    /// <summary>Consecutive wrong passwords; reset on success or when the account gets locked.</summary>
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
}

public class Instructor
{
    public int Id { get; init; }
    /// <summary>Login account of the instructor. Null for the fictional demo instructors.</summary>
    public int? UserId { get; set; }
    public required string Name { get; set; }
    public string? PhotoUrl { get; init; }
    public string Bio { get; set; } = "";
    public LicenseCategory Categories { get; set; }
    public decimal PricePerLesson { get; set; }
    public required string Neighborhood { get; set; }
    /// <summary>Meeting point coordinates: distance and service radius are measured from here.</summary>
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double ServiceRadiusKm { get; set; }
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public int LessonsGiven { get; set; }
    public string Vehicle { get; set; } = "";
    public bool HasDualControl { get; set; }
    public InstructorStatus Status { get; set; } = InstructorStatus.Pending;
    public string? RejectionReason { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedByUserId { get; set; }
    /// <summary>Fictional demo instructor: gets a generated sample agenda instead of a weekly availability.</summary>
    public bool IsDemo { get; set; }
    public string DetranCredential { get; set; } = "";
    public string MeetingPoint { get; set; } = "";
    public int LessonMinutes { get; init; } = 50;

    public bool IsApproved => Status == InstructorStatus.Approved;

    public string Initials =>
        string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => p[0]));
}

/// <summary>
/// One weekly time range in São Paulo local time (e.g. Monday 08:00–12:00). Slots are generated from these rules.
/// </summary>
public class WeeklyAvailability
{
    public int Id { get; init; }
    public int InstructorId { get; init; }
    public DayOfWeek DayOfWeek { get; init; }
    /// <summary>Minutes after local midnight.</summary>
    public int StartMinute { get; init; }
    public int EndMinute { get; init; }
}

/// <summary>An exception to the weekly availability (e.g. a medical exam). All dates are UTC.</summary>
public class TimeOff
{
    public int Id { get; init; }
    public int InstructorId { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    /// <summary>Private note, only the instructor sees it.</summary>
    public string? Note { get; init; }
    public DateTime CreatedAt { get; init; }

    public bool Overlaps(DateTime start, DateTime end) => Start < end && End > start;
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
    /// <summary>False when the slot left the instructor's availability. Kept (not deleted) because past bookings may reference it.</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>Extra slot opened by hand, outside the weekly availability.</summary>
    public bool IsManual { get; set; }
    public int Version { get; set; }

    /// <summary>A slot is free when available, or when a previous hold has already expired.</summary>
    public bool IsFree(DateTime nowUtc) =>
        IsActive &&
        (Status == SlotStatus.Available ||
         (Status == SlotStatus.Held && HeldUntil is { } until && until <= nowUtc));
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
    public CanceledBy CanceledBy { get; set; } = CanceledBy.None;
    /// <summary>Simulated refund. Null when nothing was paid.</summary>
    public decimal? RefundAmount { get; set; }
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
