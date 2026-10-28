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
    public static readonly BookingOptions Options = new() { PlatformFeePercent = 15, HoldMinutes = 10 };

    private readonly SqliteConnection _connection;
    public DbContextOptions<DirijaDbContext> DbOptions { get; }
    public FakeClock Clock { get; }
    public DirijaDbContext Db { get; }
    public BookingService BookingService { get; }
    public InstructorSearchService Search { get; }
    public IAuthService Auth { get; }

    private DomainFixture(DateTime nowUtc)
    {
        Clock = new FakeClock(nowUtc);
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        DbOptions = new DbContextOptionsBuilder<DirijaDbContext>().UseSqlite(_connection).Options;
        Db = new DirijaDbContext(DbOptions);
        BookingService = CreateBookingService(Db, Clock);
        Search = new InstructorSearchService(new EfInstructorRepository(Db));
        Auth = new PasswordAuthService(new EfUserRepository(Db), Hasher);
    }

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
        new EfReviewRepository(db), new EfUnitOfWork(db), clock, Options);

    public Task<Slot> FirstFreeSlotAsync(int instructorId = 1) => FirstFreeSlotAsync(Db, Clock, instructorId);

    public static Task<Slot> FirstFreeSlotAsync(DirijaDbContext db, IClock clock, int instructorId = 1)
    {
        var now = clock.UtcNow;
        return db.Slots.Where(s => s.InstructorId == instructorId && s.Status == SlotStatus.Available && s.Start > now)
            .OrderBy(s => s.Start).FirstAsync();
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Dirija", $"Data Source={_path}");
        builder.UseSetting("Database:ResetOnStartup", "false");
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
