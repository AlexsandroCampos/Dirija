using Dirija.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Dirija.Infrastructure.Persistence;

/// <summary>
/// EF Core model. Written to be provider-neutral (SQLite now, PostgreSQL later): only LINQ, UTC dates,
/// a manual <c>Version</c> concurrency token and enums stored as strings.
/// </summary>
public class DirijaDbContext(DbContextOptions<DirijaDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Instructor> Instructors => Set<Instructor>();
    public DbSet<Slot> Slots => Set<Slot>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Review> Reviews => Set<Review>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Neighborhood).HasMaxLength(80);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<Instructor>(e =>
        {
            e.ToTable("instructors");
            e.Ignore(x => x.Initials);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Neighborhood).HasMaxLength(80).IsRequired();
            e.Property(x => x.PricePerLesson).HasPrecision(10, 2);
            e.Property(x => x.Bio).HasMaxLength(1000);
            e.Property(x => x.Vehicle).HasMaxLength(200);
            e.Property(x => x.MeetingPoint).HasMaxLength(200);
            e.Property(x => x.DetranCredential).HasMaxLength(40);
            e.Property(x => x.PhotoUrl).HasMaxLength(500);
            e.HasIndex(x => new { x.IsVerified, x.Latitude, x.Longitude });
        });

        b.Entity<Slot>(e =>
        {
            e.ToTable("slots");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<Instructor>().WithMany().HasForeignKey(x => x.InstructorId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.InstructorId, x.Start });
            // Slot.BookingId is a plain column (no FK) to avoid a Slot <-> Booking foreign key cycle.
        });

        b.Entity<Booking>(e =>
        {
            e.ToTable("bookings");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PaymentLabel).HasMaxLength(60);
            e.Property(x => x.CancelReason).HasMaxLength(120);
            e.Property(x => x.Price).HasPrecision(10, 2);
            e.Property(x => x.PlatformFee).HasPrecision(10, 2);
            e.Property(x => x.InstructorAmount).HasPrecision(10, 2);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Instructor>().WithMany().HasForeignKey(x => x.InstructorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Slot>().WithMany().HasForeignKey(x => x.SlotId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.StudentId);
            e.HasIndex(x => x.SlotId);
        });

        b.Entity<Review>(e =>
        {
            e.ToTable("reviews");
            e.Property(x => x.StudentName).HasMaxLength(120).IsRequired();
            e.Property(x => x.Comment).HasMaxLength(500);
            e.HasOne<Instructor>().WithMany().HasForeignKey(x => x.InstructorId).OnDelete(DeleteBehavior.Cascade);
            // StudentId has no FK: seeded historical reviews come from students who are not users of the demo.
            e.HasIndex(x => x.InstructorId);
            e.HasIndex(x => x.BookingId);
        });

        ApplyUtcConverters(b);
    }

    /// <summary>Every DateTime is written and read back as UTC (SQLite drops DateTime.Kind; PostgreSQL requires UTC).</summary>
    private static void ApplyUtcConverters(ModelBuilder b)
    {
        var utc = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var utcNullable = new ValueConverter<DateTime?, DateTime?>(
            v => v == null ? v : (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()),
            v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));

        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(DateTime)) property.SetValueConverter(utc);
                else if (property.ClrType == typeof(DateTime?)) property.SetValueConverter(utcNullable);
            }
        }
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        BumpVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        BumpVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>
    /// The UPDATE is filtered by the original Version; if another request already bumped it, EF raises
    /// DbUpdateConcurrencyException instead of silently overwriting.
    /// </summary>
    private void BumpVersions()
    {
        foreach (var entry in ChangeTracker.Entries<IVersioned>())
        {
            if (entry.State == EntityState.Modified) entry.Entity.Version++;
        }
    }
}
