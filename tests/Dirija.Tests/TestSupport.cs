using Dirija.Core.Abstractions;
using Dirija.Core.Entities;
using Dirija.Core.InMemory;
using Dirija.Core.Services;

namespace Dirija.Tests;

public sealed class FakeClock(DateTime now) : IClock
{
    public DateTime Now { get; set; } = now;
    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>Builds the domain graph over a fresh seeded in-memory store.</summary>
public sealed class DomainFixture
{
    // Monday morning, so the seed generates slots for the whole day.
    public static readonly DateTime DefaultNow = new(2026, 10, 26, 7, 30, 0);

    public FakeClock Clock { get; }
    public InMemoryStore Store { get; }
    public InMemoryInstructorRepository Instructors { get; }
    public InMemorySlotRepository Slots { get; }
    public InMemoryBookingRepository Bookings { get; }
    public InMemoryReviewRepository Reviews { get; }
    public InMemoryUserRepository Users { get; }
    public BookingService BookingService { get; }
    public InstructorSearchService Search { get; }
    public BookingOptions Options { get; } = new() { PlatformFeePercent = 15, HoldMinutes = 10 };

    public DomainFixture(DateTime? now = null)
    {
        Clock = new FakeClock(now ?? DefaultNow);
        Store = SeedData.Create(Clock.Now);
        Instructors = new InMemoryInstructorRepository(Store);
        Slots = new InMemorySlotRepository(Store);
        Bookings = new InMemoryBookingRepository(Store);
        Reviews = new InMemoryReviewRepository(Store);
        Users = new InMemoryUserRepository(Store);
        BookingService = new BookingService(Slots, Bookings, Instructors, Reviews, Clock, Options);
        Search = new InstructorSearchService(Instructors);
    }

    public Slot FirstFreeSlot(int instructorId = 1) =>
        Store.Slots.Values.Where(s => s.InstructorId == instructorId && s.IsFree(Clock.Now) && s.Start > Clock.Now)
            .OrderBy(s => s.Start).First();
}
