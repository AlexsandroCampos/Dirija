using Dirija.Core.Entities;
using Dirija.Core.Geo;
using Dirija.Core.Time;
using Dirija.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Dirija.Infrastructure.Seed;

/// <summary>Fictional demo data located in São Paulo neighborhoods. All names are invented.</summary>
public static class SeedData
{
    public const string DemoPassword = "123456";
    public const string DemoStudentEmail = "aluno@dirija.com";
    public const string SecondStudentEmail = "aluna2@dirija.com";
    public const int DaysAhead = 14;
    public const decimal SeedFeePercent = 15m;

    private record InstructorSeed(
        string Name, string Neighborhood, LicenseCategory Categories, decimal Price, double Radius,
        double Rating, int ReviewCount, int Lessons, string Vehicle, bool Verified, string MeetingPoint, string Bio);

    private static readonly InstructorSeed[] InstructorSeeds =
    {
        new("Carlos Mendes", "Pinheiros", LicenseCategory.B, 90, 8, 4.9, 128, 640, "Hyundai HB20 2022 (duplo comando)", true,
            "Estação Faria Lima (saída Rua dos Pinheiros)", "Instrutor há 12 anos, paciência com quem tem medo de dirigir. Foco em baliza e trânsito pesado."),
        new("Fernanda Rocha", "Moema", LicenseCategory.B, 110, 6, 4.8, 96, 410, "Chevrolet Onix 2023 (duplo comando)", true,
            "Portão 3 do Parque Ibirapuera", "Aulas tranquilas e objetivas, com simulado do percurso da prova do Detran."),
        new("João Batista", "Tatuapé", LicenseCategory.A | LicenseCategory.B, 80, 10, 4.7, 74, 520, "Fiat Argo 2021 + Honda CG 160", true,
            "Metrô Tatuapé (saída Shopping Boulevard)", "Carro e moto. Especialista em primeira habilitação e categoria A."),
        new("Patrícia Lima", "Santana", LicenseCategory.B, 85, 8, 4.6, 51, 230, "Volkswagen Polo 2022 (duplo comando)", true,
            "Metrô Santana", "Ex-instrutora de CFC, agora autônoma. Aulas para habilitados que perderam a prática."),
        new("Rafael Souza", "Butantã", LicenseCategory.A, 70, 12, 4.8, 63, 380, "Honda CG 160 Start", true,
            "Portão 1 da USP", "Aulas de moto em área fechada e depois no trânsito. Capacete e luvas disponíveis."),
        new("Juliana Prado", "Vila Mariana", LicenseCategory.B, 120, 7, 5.0, 42, 190, "Toyota Yaris 2023 automático (duplo comando)", true,
            "Metrô Ana Rosa", "Aulas com carro automático e manual. Ideal para quem está começando do zero."),
        new("Marcos Oliveira", "Lapa", LicenseCategory.B, 75, 9, 4.4, 37, 300, "Renault Kwid 2021 (duplo comando)", true,
            "Estação Lapa (CPTM)", "Valor acessível e pacotes flexíveis. Atendo também Perdizes e Pompeia."),
        new("Aline Ferreira", "Itaim Bibi", LicenseCategory.B, 130, 6, 4.9, 88, 450, "Honda City 2023 (duplo comando)", true,
            "Praça do Itaim", "Aulas executivas em horários alternativos, inclusive cedo pela manhã."),
        new("Diego Martins", "Mooca", LicenseCategory.A | LicenseCategory.B, 95, 9, 4.5, 29, 160, "Hyundai HB20 2021 + Yamaha Factor 150", true,
            "Metrô Bresser-Mooca", "Treino focado no exame prático: baliza, rampa e conversões."),
        new("Camila Nunes", "Saúde", LicenseCategory.B, 100, 8, 4.7, 55, 270, "Fiat Mobi 2022 (duplo comando)", true,
            "Metrô Saúde", "Atendo Saúde, Jabaquara e Vila Mariana. Aulas com muita conversa e calma."),
        new("Bruno Alves", "Perdizes", LicenseCategory.B, 88, 7, 4.3, 18, 95, "Chevrolet Onix 2020 (duplo comando)", true,
            "PUC-SP (Rua Monte Alegre)", "Instrutor recém-credenciado, participante do Programa Primeiros Instrutores."),
        new("Renata Castro", "Ipiranga", LicenseCategory.B, 82, 8, 4.6, 33, 210, "Volkswagen Gol 2021 (duplo comando)", true,
            "Museu do Ipiranga", "Aulas no Ipiranga e região, com treino em ladeiras."),
        // Not verified yet: must never appear in search results (credential still under review).
        new("Tiago Ramos", "Centro", LicenseCategory.B, 60, 8, 0, 0, 0, "Fiat Uno 2015", false,
            "Praça da Sé", "Cadastro em análise."),
    };

    private static readonly (string Student, int Rating, string Comment)[] ReviewPool =
    {
        ("Ana P.", 5, "Muito paciente, me ajudou a perder o medo da baliza."),
        ("Lucas M.", 5, "Pontual e explica super bem. Passei de primeira!"),
        ("Beatriz S.", 4, "Ótima aula, só o ponto de encontro que era um pouco longe pra mim."),
        ("Gabriel R.", 5, "Carro novo e confortável, aula bem organizada."),
        ("Mariana C.", 4, "Gostei bastante, didática boa e tranquila."),
        ("Pedro H.", 5, "Fez o percurso da prova comigo, fez toda a diferença."),
        ("Larissa T.", 3, "Boa aula, mas atrasou uns 10 minutos."),
        ("Thiago F.", 5, "Recomendo demais, me senti seguro no trânsito."),
    };

    /// <summary>Inserts the full demo dataset into an empty database.</summary>
    public static async Task SeedAsync(DirijaDbContext db, DateTime nowUtc, IPasswordHasher<User> hasher)
    {
        // Saved one by one so ids are deterministic (1, 2, ...) — tests and links rely on them.
        var demo = await AddUserAsync(db, hasher, "Aluno Demo", DemoStudentEmail, "(11) 90000-0001", "Bela Vista");
        await AddUserAsync(db, hasher, "Aluna Demo 2", SecondStudentEmail, "(11) 90000-0002", "Moema");

        var instructors = new List<Instructor>();
        var n = 0;
        foreach (var s in InstructorSeeds)
        {
            n++;
            var hood = Neighborhoods.Find(s.Neighborhood)!;
            // Small deterministic offset so instructors in the same area don't overlap on the map.
            var rnd = new Random(n * 7919);
            var instructor = new Instructor
            {
                Name = s.Name, Neighborhood = hood.Name, Categories = s.Categories,
                PricePerLesson = s.Price, ServiceRadiusKm = s.Radius, AverageRating = s.Rating,
                ReviewCount = s.ReviewCount, LessonsGiven = s.Lessons, Vehicle = s.Vehicle,
                HasDualControl = s.Vehicle.Contains("duplo comando") || s.Categories.HasFlag(LicenseCategory.A),
                IsVerified = s.Verified, MeetingPoint = s.MeetingPoint, Bio = s.Bio,
                DetranCredential = s.Verified ? $"SP-{482000 + n * 137}" : "",
                Latitude = hood.Location.Latitude + (rnd.NextDouble() - 0.5) * 0.008,
                Longitude = hood.Location.Longitude + (rnd.NextDouble() - 0.5) * 0.008,
            };
            db.Instructors.Add(instructor);
            await db.SaveChangesAsync();
            instructors.Add(instructor);
        }

        await TopUpSlotsAsync(db, nowUtc);

        foreach (var instructor in instructors.Where(i => i.IsVerified))
        {
            for (var k = 0; k < 3; k++)
            {
                var r = ReviewPool[(instructor.Id * 3 + k) % ReviewPool.Length];
                db.Reviews.Add(new Review
                {
                    InstructorId = instructor.Id, StudentId = 100 + instructor.Id * 3 + k,
                    StudentName = r.Student, Rating = r.Rating, Comment = r.Comment,
                    CreatedAt = nowUtc.AddDays(-(k * 9 + instructor.Id % 5 + 2))
                });
            }
        }
        await db.SaveChangesAsync();

        await SeedDemoBookingsAsync(db, demo, instructors, nowUtc);
    }

    /// <summary>
    /// Generates slots for the next <see cref="DaysAhead"/> local days that have none yet, so a database created
    /// days ago still shows a full agenda. Idempotent.
    /// </summary>
    public static async Task<int> TopUpSlotsAsync(DirijaDbContext db, DateTime nowUtc)
    {
        var todayLocal = SaoPauloTime.Today(nowUtc);
        var fromUtc = SaoPauloTime.ToUtc(todayLocal);

        var existing = await db.Slots.Where(s => s.Start >= fromUtc)
            .Select(s => new { s.InstructorId, s.Start }).ToListAsync();
        var covered = existing.Select(x => (x.InstructorId, SaoPauloTime.ToLocal(x.Start).Date)).ToHashSet();
        var instructors = await db.Instructors.Select(i => new { i.Id, i.LessonMinutes }).ToListAsync();

        var created = 0;
        for (var d = 0; d < DaysAhead; d++)
        {
            var date = todayLocal.AddDays(d);
            foreach (var i in instructors)
            {
                if (covered.Contains((i.Id, date))) continue;
                foreach (var slot in GenerateDay(i.Id, i.LessonMinutes, date, nowUtc))
                {
                    db.Slots.Add(slot);
                    created++;
                }
            }
        }
        if (created > 0) await db.SaveChangesAsync();
        return created;
    }

    /// <summary>One local day of hourly slots (08:00–17:00, Saturday until 12:00, no Sundays).</summary>
    private static IEnumerable<Slot> GenerateDay(int instructorId, int lessonMinutes, DateTime localDate, DateTime nowUtc)
    {
        if (localDate.DayOfWeek == DayOfWeek.Sunday) yield break;
        var lastHour = localDate.DayOfWeek == DayOfWeek.Saturday ? 12 : 17;
        var rnd = new Random(instructorId * 100_003 + (int)(localDate - DateTime.UnixEpoch).TotalDays);

        for (var hour = 8; hour <= lastHour; hour++)
        {
            var roll = rnd.NextDouble();
            var startUtc = SaoPauloTime.ToUtc(localDate.AddHours(hour));
            if (startUtc <= nowUtc) continue;
            yield return new Slot
            {
                InstructorId = instructorId, Start = startUtc, End = startUtc.AddMinutes(lessonMinutes),
                Status = roll < 0.15 ? SlotStatus.Booked     // booked by other (fictional) students
                       : roll < 0.25 ? SlotStatus.Blocked    // instructor unavailable
                       : SlotStatus.Available
            };
        }
    }

    private static async Task<User> AddUserAsync(DirijaDbContext db, IPasswordHasher<User> hasher,
        string name, string email, string phone, string neighborhood)
    {
        var user = new User { Name = name, Email = email, PasswordHash = "", Phone = phone, Neighborhood = neighborhood };
        user.PasswordHash = hasher.HashPassword(user, DemoPassword);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task SeedDemoBookingsAsync(DirijaDbContext db, User student, List<Instructor> instructors, DateTime nowUtc)
    {
        var todayLocal = SaoPauloTime.Today(nowUtc);

        async Task<Slot> PastSlotAsync(Instructor instructor, int daysAgo, int hour)
        {
            var start = SaoPauloTime.ToUtc(todayLocal.AddDays(-daysAgo).AddHours(hour));
            var slot = new Slot { InstructorId = instructor.Id, Start = start, End = start.AddMinutes(instructor.LessonMinutes) };
            db.Slots.Add(slot);
            await db.SaveChangesAsync();
            return slot;
        }

        async Task<Booking> BookAsync(Slot slot, Instructor instructor, BookingStatus status, DateTime createdAt)
        {
            var fee = Math.Round(instructor.PricePerLesson * SeedFeePercent / 100m, 2, MidpointRounding.AwayFromZero);
            var booking = new Booking
            {
                StudentId = student.Id, InstructorId = instructor.Id, SlotId = slot.Id,
                Price = instructor.PricePerLesson, PlatformFee = fee, InstructorAmount = instructor.PricePerLesson - fee,
                Status = status, PaymentMethod = PaymentMethod.Pix, PaymentLabel = "PIX",
                CreatedAt = createdAt, PaidAt = createdAt.AddMinutes(3)
            };
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            slot.Status = SlotStatus.Booked;
            slot.BookingId = booking.Id;
            await db.SaveChangesAsync();
            return booking;
        }

        var carlos = instructors[0];
        var fernanda = instructors[1];
        var joao = instructors[2];

        // Completed lesson not yet reviewed (demo of "Avaliar aula").
        await BookAsync(await PastSlotAsync(carlos, 3, 10), carlos, BookingStatus.Completed, nowUtc.AddDays(-5));

        // Completed lesson already reviewed by the demo student.
        var reviewed = await BookAsync(await PastSlotAsync(joao, 8, 14), joao, BookingStatus.Completed, nowUtc.AddDays(-10));
        db.Reviews.Add(new Review
        {
            InstructorId = joao.Id, StudentId = student.Id, StudentName = student.Name, BookingId = reviewed.Id,
            Rating = 5, Comment = "Primeira aula de moto, me senti muito seguro.", CreatedAt = nowUtc.AddDays(-8).AddHours(4)
        });
        await db.SaveChangesAsync();

        // Upcoming confirmed lesson so "Próximas" is not empty.
        var fromUtc = SaoPauloTime.ToUtc(todayLocal.AddDays(2));
        var upcoming = await db.Slots
            .Where(s => s.InstructorId == fernanda.Id && s.Status == SlotStatus.Available && s.Start >= fromUtc)
            .OrderBy(s => s.Start).FirstOrDefaultAsync();
        if (upcoming is not null) await BookAsync(upcoming, fernanda, BookingStatus.Confirmed, nowUtc.AddDays(-1));
    }
}
