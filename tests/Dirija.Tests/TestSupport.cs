using Dirija.Core.Abstractions;
using Dirija.Core.Entities;
using Dirija.Core.Services;
using Dirija.Infrastructure.Identity;
using Dirija.Infrastructure.Persistence;
using Dirija.Infrastructure.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dirija.Tests;

public sealed class FakeClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow;
    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>
/// Domain graph over a fresh, migrated and seeded SQLite in-memory database (one per test).
/// </summary>
public sealed class DomainFixture : IAsyncDisposable
{
    /// <summary>Monday 26/10/2026 07:30 in São Paulo (UTC-3), so the seed generates slots for the whole day.</summary>
    public static readonly DateTime DefaultNowUtc = new(2026, 10, 26, 10, 30, 0, DateTimeKind.Utc);

    public static readonly IPasswordHasher<User> Hasher = new PasswordHasher<User>();
    public static readonly IPasswordService Passwords = new IdentityPasswordService(Hasher);
    public static readonly BookingOptions Options = new()
    {
        PlatformFeePercent = 15, HoldMinutes = 10, MinNoticeHours = 6, StudentFullRefundHours = 24
    };
    public static readonly AgendaOptions Agenda = new() { WindowDays = 60 };

    private readonly SqliteConnection _connection;
    public DbContextOptions<DirijaDbContext> DbOptions { get; }
    public FakeClock Clock { get; }
    public DirijaDbContext Db { get; }
    public BookingService BookingService { get; }
    public InstructorSearchService Search { get; }
    public IAuthService Auth { get; }
    public AvailabilityService Availability { get; }
    public AccountService Accounts { get; }
    public InstructorProfileService Profiles { get; }

    private DomainFixture(DateTime nowUtc)
    {
        Clock = new FakeClock(nowUtc);
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        DbOptions = new DbContextOptionsBuilder<DirijaDbContext>().UseSqlite(_connection).Options;
        Db = new DirijaDbContext(DbOptions);
        BookingService = CreateBookingService(Db, Clock);
        Search = new InstructorSearchService(new EfInstructorRepository(Db));
        Auth = new PasswordAuthService(new EfUserRepository(Db), Passwords, new EfUnitOfWork(Db), Clock);
        Availability = CreateAvailability(Db, Clock);
        Accounts = new AccountService(new EfUserRepository(Db), new EfInstructorRepository(Db), Passwords, new EfUnitOfWork(Db), Clock);
        Profiles = new InstructorProfileService(new EfInstructorRepository(Db), new EfUserRepository(Db), new EfUnitOfWork(Db), Clock);
    }

    public static AvailabilityService CreateAvailability(DirijaDbContext db, IClock clock) => new(
        new EfInstructorRepository(db), new EfSlotRepository(db), new EfAgendaRepository(db),
        new EfBookingRepository(db), new EfUnitOfWork(db), clock, Agenda);

    public static async Task<DomainFixture> CreateAsync(DateTime? nowUtc = null)
    {
        var f = new DomainFixture(nowUtc ?? DefaultNowUtc);
        await DbInitializer.InitializeAsync(f.Db, f.Clock, Hasher);
        return f;
    }

    /// <summary>A second, independent context over the same in-memory database.</summary>
    public DirijaDbContext NewContext() => new(DbOptions);

    public static BookingService CreateBookingService(DirijaDbContext db, IClock clock) => new(
        new EfSlotRepository(db), new EfBookingRepository(db), new EfInstructorRepository(db),
        new EfReviewRepository(db), new EfAgendaRepository(db), new EfUnitOfWork(db), clock, Options);

    public Task<Slot> FirstFreeSlotAsync(int instructorId = 1) => FirstFreeSlotAsync(Db, Clock, instructorId);

    /// <summary>First free slot that respects the minimum notice (i.e. a slot a student can actually book).</summary>
    public static Task<Slot> FirstFreeSlotAsync(DirijaDbContext db, IClock clock, int instructorId = 1)
    {
        var from = clock.UtcNow.AddHours(Options.MinNoticeHours);
        return db.Slots.Where(s => s.InstructorId == instructorId && s.IsActive && s.Status == SlotStatus.Available && s.Start > from)
            .OrderBy(s => s.Start).FirstAsync();
    }

    /// <summary>A free bookable slot starting at least <paramref name="minHoursAhead"/> hours from now.</summary>
    public Task<Slot> FreeSlotAtLeastAsync(double minHoursAhead, int instructorId = 1)
    {
        var from = Clock.UtcNow.AddHours(Math.Max(minHoursAhead, Options.MinNoticeHours));
        return Db.Slots.Where(s => s.InstructorId == instructorId && s.IsActive && s.Status == SlotStatus.Available && s.Start > from)
            .OrderBy(s => s.Start).FirstAsync();
    }

    /// <summary>Holds and pays a slot for the student; returns the confirmed booking.</summary>
    public async Task<Booking> BookAsync(int studentId, Slot slot)
    {
        var booking = (await BookingService.HoldSlotAsync(studentId, slot.Id)).Booking!;
        await BookingService.ConfirmPaymentAsync(booking.Id, studentId, PaymentMethod.Pix, "PIX");
        return booking;
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

/// <summary>Web app over a temporary SQLite file, deleted on dispose.</summary>
public class DirijaWebFactory : WebApplicationFactory<Program>
{
    private readonly string _path;
    private readonly bool _deleteOnDispose;

    // xUnit class fixtures must have a single public constructor.
    public DirijaWebFactory() : this(Path.Combine(Path.GetTempPath(), $"dirija-it-{Guid.NewGuid():N}.db"), true) { }

    protected DirijaWebFactory(string path, bool deleteOnDispose)
    {
        _path = path;
        _deleteOnDispose = deleteOnDispose;
    }

    /// <summary>Extra configuration for a specific test (e.g. Admin:Email).</summary>
    protected virtual IReadOnlyDictionary<string, string?> ExtraSettings => new Dictionary<string, string?>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Dirija", $"Data Source={_path}");
        builder.UseSetting("Database:ResetOnStartup", "false");
        foreach (var (key, value) in ExtraSettings) builder.UseSetting(key, value);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (_deleteOnDispose) TempDb.Delete(_path);
        else SqliteConnection.ClearAllPools();
    }
}

/// <summary>App over an explicit database file that survives dispose (simulates restarting the app).</summary>
public sealed class PersistentWebFactory(string path) : DirijaWebFactory(path, deleteOnDispose: false);

public static class TempDb
{
    public static void Delete(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { path, path + "-wal", path + "-shm" })
        {
            try { File.Delete(f); } catch (IOException) { }
        }
    }
}
