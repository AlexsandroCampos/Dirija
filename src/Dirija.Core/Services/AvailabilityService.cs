using Dirija.Core.Abstractions;
using Dirija.Core.Entities;
using Dirija.Core.Time;

namespace Dirija.Core.Services;

public class AgendaOptions
{
    public const string SectionName = "Agenda";
    /// <summary>How many days ahead students can book (rolling window kept filled with slots).</summary>
    public int WindowDays { get; set; } = 60;
    /// <summary>Slots start every this many minutes inside a range (50-min lesson + 10-min break).</summary>
    public int SlotStepMinutes { get; set; } = 60;
    public int EarliestMinute { get; set; } = 6 * 60;
    public int LatestMinute { get; set; } = 22 * 60;
    public int MaxRangesPerDay { get; set; } = 2;
    public int MaxTimeOffDays { get; set; } = 90;
}

/// <summary>A weekly range in São Paulo local time, in minutes after midnight.</summary>
public record AvailabilityRange(DayOfWeek Day, int StartMinute, int EndMinute);

public enum AgendaError
{
    None,
    NotFound,
    InvalidRange,
    InPast,
    OutsideWindow,
    TooLong,
    NoteTooLong,
    SlotBusy,
    AlreadyBlocked,
    CoveredByPeriod,
    AlreadyExists,
    NotManual,
    /// <summary>A student reserved a slot in the period at the same moment; nothing was saved.</summary>
    Conflict
}

public record AgendaResult(AgendaError Error, TimeOff? TimeOff = null, Slot? Slot = null)
{
    public bool Success => Error == AgendaError.None;
    public static AgendaResult Ok(TimeOff? timeOff = null, Slot? slot = null) => new(AgendaError.None, timeOff, slot);
    public static AgendaResult Fail(AgendaError error) => new(error);
}

/// <summary>One line of the instructor's agenda: the slot, its booking (if any) and the time off that blocks it (if any).</summary>
public record AgendaEntry(Slot Slot, Booking? Booking, TimeOff? TimeOff);

/// <summary>
/// Instructor agenda: weekly availability generates slots for a rolling window; time offs and extra slots are
/// exceptions on top of it. Slots stay the unit of booking (and of the optimistic concurrency check).
/// </summary>
public class AvailabilityService(
    IInstructorRepository instructors,
    ISlotRepository slots,
    IAgendaRepository agenda,
    IBookingRepository bookings,
    IUnitOfWork unitOfWork,
    IClock clock,
    AgendaOptions options)
{
    public const int MaxNoteLength = 100;
    public AgendaOptions Options => options;

    // ------------------------------------------------------------------ weekly availability

    public Task<IReadOnlyList<WeeklyAvailability>> GetWeeklyAsync(int instructorId) => agenda.GetRulesAsync(instructorId);

    /// <summary>Validates the ranges: full hours, inside the allowed day, at most N per day, no overlaps.</summary>
    public AgendaError ValidateRanges(IReadOnlyCollection<AvailabilityRange> ranges)
    {
        foreach (var r in ranges)
        {
            if (r.StartMinute % 60 != 0 || r.EndMinute % 60 != 0) return AgendaError.InvalidRange;
            if (r.StartMinute < options.EarliestMinute || r.EndMinute > options.LatestMinute) return AgendaError.InvalidRange;
            if (r.EndMinute - r.StartMinute < options.SlotStepMinutes) return AgendaError.InvalidRange;
        }
        foreach (var day in ranges.GroupBy(r => r.Day))
        {
            if (day.Count() > options.MaxRangesPerDay) return AgendaError.InvalidRange;
            var ordered = day.OrderBy(r => r.StartMinute).ToList();
            for (var i = 1; i < ordered.Count; i++)
                if (ordered[i].StartMinute < ordered[i - 1].EndMinute) return AgendaError.InvalidRange;
        }
        return AgendaError.None;
    }

    /// <summary>Replaces the weekly availability and regenerates the free future slots. Booked slots are kept.</summary>
    public async Task<AgendaResult> SaveWeeklyAsync(int instructorId, IReadOnlyCollection<AvailabilityRange> ranges)
    {
        if (await instructors.GetByIdAsync(instructorId) is not { IsDemo: false }) return AgendaResult.Fail(AgendaError.NotFound);
        var error = ValidateRanges(ranges);
        if (error != AgendaError.None) return AgendaResult.Fail(error);

        agenda.RemoveRules(await agenda.GetRulesAsync(instructorId));
        foreach (var r in ranges)
            agenda.AddRule(new WeeklyAvailability { InstructorId = instructorId, DayOfWeek = r.Day, StartMinute = r.StartMinute, EndMinute = r.EndMinute });
        await unitOfWork.SaveChangesAsync();

        await SyncAsync(instructorId);
        return AgendaResult.Ok();
    }

    /// <summary>
    /// Makes the slots of the next <see cref="AgendaOptions.WindowDays"/> days match the weekly availability:
    /// creates the missing ones, deactivates free slots that left the availability (kept for history) and blocks
    /// free slots covered by a time off. Idempotent; returns false if another request changed the agenda meanwhile.
    /// </summary>
    public async Task<bool> SyncAsync(int instructorId)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await SyncOnceAsync(instructorId);
                return true;
            }
            catch (Exception ex) when (ex is ConcurrencyConflictException or UniqueConstraintException)
            {
                unitOfWork.DiscardChanges();
            }
        }
        return false;
    }

    public async Task SyncAllAsync()
    {
        foreach (var id in await instructors.GetIdsWithRealAgendaAsync()) await SyncAsync(id);
    }

    private async Task SyncOnceAsync(int instructorId)
    {
        var instructor = await instructors.GetByIdAsync(instructorId);
        if (instructor is null || instructor.IsDemo) return;

        var now = clock.UtcNow;
        var today = SaoPauloTime.Today(now);
        var untilUtc = SaoPauloTime.ToUtc(today.AddDays(options.WindowDays));
        var rules = await agenda.GetRulesAsync(instructorId);
        var timeOffs = await agenda.GetTimeOffsAsync(instructorId, now);
        var lesson = TimeSpan.FromMinutes(instructor.LessonMinutes);

        var desired = new HashSet<DateTime>();
        for (var d = 0; d < options.WindowDays; d++)
        {
            var date = today.AddDays(d);
            foreach (var rule in rules.Where(r => r.DayOfWeek == date.DayOfWeek))
            {
                for (var m = rule.StartMinute; m + instructor.LessonMinutes <= rule.EndMinute; m += options.SlotStepMinutes)
                {
                    var startUtc = SaoPauloTime.ToUtc(date.AddMinutes(m));
                    if (startUtc > now) desired.Add(startUtc);
                }
            }
        }

        var existing = (await slots.GetRangeAsync(instructorId, now, untilUtc)).ToDictionary(s => s.Start);
        var changed = false;

        foreach (var start in desired.Where(s => !existing.ContainsKey(s)))
        {
            var end = start + lesson;
            slots.Add(new Slot
            {
                InstructorId = instructorId, Start = start, End = end,
                Status = timeOffs.Any(t => t.Overlaps(start, end)) ? SlotStatus.Blocked : SlotStatus.Available
            });
            changed = true;
        }

        foreach (var slot in existing.Values.Where(s => s.Status is SlotStatus.Available or SlotStatus.Blocked))
        {
            var wanted = slot.IsManual || desired.Contains(slot.Start);
            if (slot.IsActive != wanted)
            {
                slot.IsActive = wanted;
                changed = true;
            }
            if (slot.IsActive && slot.Status == SlotStatus.Available && timeOffs.Any(t => t.Overlaps(slot.Start, slot.End)))
            {
                slot.Status = SlotStatus.Blocked;
                changed = true;
            }
        }

        if (changed) await unitOfWork.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ time offs (exceptions)

    public Task<IReadOnlyList<TimeOff>> GetUpcomingTimeOffsAsync(int instructorId) =>
        agenda.GetTimeOffsAsync(instructorId, clock.UtcNow);

    /// <summary>Booked or held lessons that a time off in this period would hit (shown before saving it).</summary>
    public async Task<IReadOnlyList<(Booking Booking, Slot Slot)>> GetAffectedBookingsAsync(int instructorId, DateTime startUtc, DateTime endUtc)
    {
        var now = clock.UtcNow;
        var hit = (await SlotsOverlappingAsync(instructorId, startUtc, endUtc))
            .Where(s => s.Start > now && s.BookingId is not null &&
                        (s.Status == SlotStatus.Booked || (s.Status == SlotStatus.Held && s.HeldUntil > now)))
            .ToList();
        var map = (await bookings.GetByIdsAsync(hit.Select(s => s.BookingId!.Value))).ToDictionary(b => b.Id);
        return hit.Where(s => map.ContainsKey(s.BookingId!.Value))
            .Select(s => (map[s.BookingId!.Value], s))
            .Where(x => x.Item1.Status is BookingStatus.Confirmed or BookingStatus.PendingPayment)
            .OrderBy(x => x.s.Start)
            .ToList();
    }

    /// <summary>
    /// Blocks a period. Free slots inside it become unavailable at once; lessons already booked are kept (the
    /// caller cancels them first if the instructor chose so). If a student reserves a slot of the period at the same
    /// moment, nothing is saved and <see cref="AgendaError.Conflict"/> is returned.
    /// </summary>
    public async Task<AgendaResult> AddTimeOffAsync(int instructorId, DateTime startUtc, DateTime endUtc, string? note)
    {
        var now = clock.UtcNow;
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (text is { Length: > MaxNoteLength }) return AgendaResult.Fail(AgendaError.NoteTooLong);
        if (endUtc <= startUtc) return AgendaResult.Fail(AgendaError.InvalidRange);
        if (endUtc <= now) return AgendaResult.Fail(AgendaError.InPast);
        if (endUtc - startUtc > TimeSpan.FromDays(options.MaxTimeOffDays)) return AgendaResult.Fail(AgendaError.TooLong);
        if (startUtc > now.AddDays(365)) return AgendaResult.Fail(AgendaError.OutsideWindow);
        if (await instructors.GetByIdAsync(instructorId) is null) return AgendaResult.Fail(AgendaError.NotFound);

        var timeOff = new TimeOff { InstructorId = instructorId, Start = startUtc, End = endUtc, Note = text, CreatedAt = now };
        try
        {
            await using var tx = await unitOfWork.BeginTransactionAsync();
            agenda.AddTimeOff(timeOff);
            foreach (var slot in await SlotsOverlappingAsync(instructorId, startUtc, endUtc))
            {
                if (slot.Start <= now || !slot.IsFree(now)) continue;
                slot.Status = SlotStatus.Blocked; // version check: fails if a student took it meanwhile
                slot.HeldUntil = null;
                slot.BookingId = null;
            }
            await unitOfWork.SaveChangesAsync();
            await tx.CommitAsync();
            return AgendaResult.Ok(timeOff);
        }
        catch (ConcurrencyConflictException)
        {
            unitOfWork.DiscardChanges();
            return AgendaResult.Fail(AgendaError.Conflict);
        }
    }

    /// <summary>Removes a time off; the slots it blocked become free again unless another time off still covers them.</summary>
    public async Task<AgendaResult> RemoveTimeOffAsync(int instructorId, int timeOffId)
    {
        var timeOff = await agenda.GetTimeOffAsync(timeOffId);
        if (timeOff is null || timeOff.InstructorId != instructorId) return AgendaResult.Fail(AgendaError.NotFound);

        var now = clock.UtcNow;
        var others = (await agenda.GetOverlappingTimeOffsAsync(instructorId, timeOff.Start, timeOff.End))
            .Where(t => t.Id != timeOff.Id).ToList();
        try
        {
            agenda.RemoveTimeOff(timeOff);
            foreach (var slot in await SlotsOverlappingAsync(instructorId, timeOff.Start, timeOff.End))
            {
                if (slot.Start > now && slot.Status == SlotStatus.Blocked && !others.Any(t => t.Overlaps(slot.Start, slot.End)))
                    slot.Status = SlotStatus.Available;
            }
            await unitOfWork.SaveChangesAsync();
            return AgendaResult.Ok(timeOff);
        }
        catch (ConcurrencyConflictException)
        {
            unitOfWork.DiscardChanges();
            return AgendaResult.Fail(AgendaError.Conflict);
        }
    }

    /// <summary>One tap on a free slot: blocks just that slot (stored as a time off of the slot's length).</summary>
    public async Task<AgendaResult> BlockSlotAsync(int instructorId, int slotId)
    {
        var slot = await slots.GetByIdAsync(slotId);
        if (slot is null || slot.InstructorId != instructorId || !slot.IsActive) return AgendaResult.Fail(AgendaError.NotFound);
        if (slot.Start <= clock.UtcNow) return AgendaResult.Fail(AgendaError.InPast);
        if (slot.Status == SlotStatus.Blocked) return AgendaResult.Fail(AgendaError.AlreadyBlocked);
        if (!slot.IsFree(clock.UtcNow)) return AgendaResult.Fail(AgendaError.SlotBusy);
        return await AddTimeOffAsync(instructorId, slot.Start, slot.End, null);
    }

    /// <summary>Unblocks a single slot. Slots inside a longer period are unblocked by removing that period.</summary>
    public async Task<AgendaResult> UnblockSlotAsync(int instructorId, int slotId)
    {
        var slot = await slots.GetByIdAsync(slotId);
        if (slot is null || slot.InstructorId != instructorId) return AgendaResult.Fail(AgendaError.NotFound);
        if (slot.Start <= clock.UtcNow) return AgendaResult.Fail(AgendaError.InPast);
        if (slot.Status != SlotStatus.Blocked) return AgendaResult.Fail(AgendaError.SlotBusy);

        var covering = await agenda.GetOverlappingTimeOffsAsync(instructorId, slot.Start, slot.End);
        if (covering.Any(t => t.Start != slot.Start || t.End != slot.End)) return AgendaResult.Fail(AgendaError.CoveredByPeriod);

        try
        {
            foreach (var t in covering) agenda.RemoveTimeOff(t);
            slot.Status = SlotStatus.Available;
            await unitOfWork.SaveChangesAsync();
            return AgendaResult.Ok(slot: slot);
        }
        catch (ConcurrencyConflictException)
        {
            unitOfWork.DiscardChanges();
            return AgendaResult.Fail(AgendaError.Conflict);
        }
    }

    // ------------------------------------------------------------------ extra slots

    /// <summary>Opens one slot outside the weekly availability (full hours only, inside the booking window).</summary>
    public async Task<AgendaResult> AddExtraSlotAsync(int instructorId, DateTime localStart)
    {
        var instructor = await instructors.GetByIdAsync(instructorId);
        if (instructor is null || instructor.IsDemo) return AgendaResult.Fail(AgendaError.NotFound);

        var now = clock.UtcNow;
        var minute = localStart.Hour * 60 + localStart.Minute;
        if (localStart.Minute != 0 || localStart.Second != 0 || minute < options.EarliestMinute ||
            minute + instructor.LessonMinutes > options.LatestMinute)
        {
            return AgendaResult.Fail(AgendaError.InvalidRange);
        }

        var startUtc = SaoPauloTime.ToUtc(localStart);
        if (startUtc <= now) return AgendaResult.Fail(AgendaError.InPast);
        if (localStart.Date >= SaoPauloTime.Today(now).AddDays(options.WindowDays)) return AgendaResult.Fail(AgendaError.OutsideWindow);

        var end = startUtc.AddMinutes(instructor.LessonMinutes);
        var blocked = (await agenda.GetOverlappingTimeOffsAsync(instructorId, startUtc, end)).Count > 0;
        var existing = (await slots.GetRangeAsync(instructorId, startUtc, startUtc.AddSeconds(1))).FirstOrDefault();
        try
        {
            if (existing is not null)
            {
                if (existing.IsActive) return AgendaResult.Fail(AgendaError.AlreadyExists);
                existing.IsActive = true;
                existing.IsManual = true;
                existing.Status = blocked ? SlotStatus.Blocked : SlotStatus.Available;
                await unitOfWork.SaveChangesAsync();
                return AgendaResult.Ok(slot: existing);
            }

            var slot = new Slot
            {
                InstructorId = instructorId, Start = startUtc, End = end, IsManual = true,
                Status = blocked ? SlotStatus.Blocked : SlotStatus.Available
            };
            slots.Add(slot);
            await unitOfWork.SaveChangesAsync();
            return AgendaResult.Ok(slot: slot);
        }
        catch (Exception ex) when (ex is ConcurrencyConflictException or UniqueConstraintException)
        {
            unitOfWork.DiscardChanges();
            return AgendaResult.Fail(AgendaError.AlreadyExists);
        }
    }

    public async Task<AgendaResult> RemoveExtraSlotAsync(int instructorId, int slotId)
    {
        var slot = await slots.GetByIdAsync(slotId);
        if (slot is null || slot.InstructorId != instructorId) return AgendaResult.Fail(AgendaError.NotFound);
        if (!slot.IsManual) return AgendaResult.Fail(AgendaError.NotManual);
        if (slot.Status is SlotStatus.Booked or SlotStatus.Held && !slot.IsFree(clock.UtcNow)) return AgendaResult.Fail(AgendaError.SlotBusy);

        try
        {
            slot.IsActive = false;
            slot.IsManual = false;
            slot.Status = SlotStatus.Available;
            slot.HeldUntil = null;
            slot.BookingId = null;
            await unitOfWork.SaveChangesAsync();
            return AgendaResult.Ok(slot: slot);
        }
        catch (ConcurrencyConflictException)
        {
            unitOfWork.DiscardChanges();
            return AgendaResult.Fail(AgendaError.Conflict);
        }
    }

    // ------------------------------------------------------------------ instructor view

    /// <summary>Agenda entries (active slots plus any booked one) for local days [fromLocalDate, fromLocalDate + days).</summary>
    public async Task<IReadOnlyList<AgendaEntry>> GetEntriesAsync(int instructorId, DateTime fromLocalDate, int days)
    {
        var fromUtc = SaoPauloTime.ToUtc(fromLocalDate.Date);
        var toUtc = SaoPauloTime.ToUtc(fromLocalDate.Date.AddDays(days));
        var list = (await slots.GetRangeAsync(instructorId, fromUtc, toUtc))
            .Where(s => s.IsActive || s.BookingId is not null)
            .OrderBy(s => s.Start)
            .ToList();
        var bookingMap = (await bookings.GetByIdsAsync(list.Where(s => s.BookingId is not null).Select(s => s.BookingId!.Value)))
            .ToDictionary(b => b.Id);
        var timeOffs = await agenda.GetOverlappingTimeOffsAsync(instructorId, fromUtc, toUtc);

        return list.Select(s => new AgendaEntry(
                s,
                s.BookingId is { } id && bookingMap.TryGetValue(id, out var b) ? b : null,
                s.Status == SlotStatus.Blocked ? timeOffs.FirstOrDefault(t => t.Overlaps(s.Start, s.End)) : null))
            .ToList();
    }

    private async Task<IReadOnlyList<Slot>> SlotsOverlappingAsync(int instructorId, DateTime startUtc, DateTime endUtc) =>
        (await slots.GetRangeAsync(instructorId, startUtc.AddHours(-2), endUtc))
            .Where(s => s.Start < endUtc && s.End > startUtc)
            .ToList();
}
