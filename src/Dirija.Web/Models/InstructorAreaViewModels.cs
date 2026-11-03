using Dirija.Core.Entities;
using Dirija.Core.Services;

namespace Dirija.Web.Models;

public record AgendaDay(DateTime Date, IReadOnlyList<AgendaEntry> Entries);

public class InstructorAgendaViewModel
{
    public required Instructor Instructor { get; init; }
    public required IReadOnlyList<AgendaDay> Days { get; init; }
    public required IReadOnlyDictionary<int, User> Students { get; init; }
    public bool HasWeeklyAvailability { get; init; }
    public int Week { get; init; }
    public int MaxWeek { get; init; }
    public DateTime Today { get; init; }
    public DateTime Now { get; init; }
    public int EarliestHour { get; init; }
    public int LatestHour { get; init; }
    public int WindowDays { get; init; }
}

public class WeeklyDayRow
{
    public DayOfWeek Day { get; set; }
    public bool Enabled { get; set; }
    public int? Start1 { get; set; }
    public int? End1 { get; set; }
    public int? Start2 { get; set; }
    public int? End2 { get; set; }
}

public class WeeklyAvailabilityViewModel
{
    public List<WeeklyDayRow> Days { get; set; } = new();
    public int EarliestHour { get; set; }
    public int LatestHour { get; set; }
}

public class TimeOffFormModel
{
    public DateTime? StartDate { get; set; }
    /// <summary>Optional: last day of a multi-day period. Empty means the same day.</summary>
    public DateTime? EndDate { get; set; }
    public bool AllDay { get; set; }
    public int StartHour { get; set; } = 8;
    public int EndHour { get; set; } = 12;
    public string? Note { get; set; }

    /// <summary>Second step, when the period hits booked lessons: "cancel" or "keep".</summary>
    public string? Decision { get; set; }
    public string? CancelReason { get; set; }
}

public record AffectedLesson(Booking Booking, Slot Slot, User? Student);

public class TimeOffViewModel
{
    public required TimeOffFormModel Form { get; init; }
    public required IReadOnlyList<TimeOff> Upcoming { get; init; }
    public IReadOnlyList<AffectedLesson> Affected { get; init; } = [];
    public bool NeedsDecision => Affected.Count > 0;
}

public record InstructorLessonItem(Booking Booking, Slot Slot, User? Student, bool ShowContact);

public class InstructorLessonsViewModel
{
    public required IReadOnlyList<InstructorLessonItem> Upcoming { get; init; }
    public required IReadOnlyList<InstructorLessonItem> History { get; init; }
    public DateTime Now { get; init; }
    public string Tab { get; init; } = "proximas";
}

public class InstructorProfilePageViewModel
{
    public required User User { get; init; }
    public required Instructor Instructor { get; init; }
}

public record AdminInstructorRow(Instructor Instructor, User? User);

public class AdminListViewModel
{
    public InstructorStatus Status { get; init; }
    public required IReadOnlyDictionary<InstructorStatus, int> Counts { get; init; }
    public required IReadOnlyList<AdminInstructorRow> Rows { get; init; }
}

public class AdminDetailsViewModel
{
    public required Instructor Instructor { get; init; }
    public User? User { get; init; }
    public string? RejectReason { get; init; }
}
