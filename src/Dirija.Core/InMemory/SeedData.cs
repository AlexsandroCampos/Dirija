using Dirija.Core.Entities;
using Dirija.Core.Geo;

namespace Dirija.Core.InMemory;

/// <summary>Fictional demo data located in São Paulo neighborhoods. All names are invented.</summary>
public static class SeedData
{
    public const int DemoStudentId = 1;
    public const int SecondStudentId = 2;
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

    public static InMemoryStore Create(DateTime now)
    {
        var store = new InMemoryStore();

        store.Users[DemoStudentId] = new User
        {
            Id = DemoStudentId, Name = "Aluno Demo", Email = "aluno@dirija.com", Password = "123456",
            Phone = "(11) 90000-0001", Neighborhood = "Bela Vista"
        };
        store.Users[SecondStudentId] = new User
        {
            Id = SecondStudentId, Name = "Aluna Demo 2", Email = "aluna2@dirija.com", Password = "123456",
            Phone = "(11) 90000-0002", Neighborhood = "Moema"
        };

        var id = 0;
        foreach (var s in InstructorSeeds)
        {
            id++;
            var hood = Neighborhoods.Find(s.Neighborhood)!;
            // Small deterministic offset so instructors in the same area don't overlap on the map.
            var rnd = new Random(id * 7919);
            store.Instructors[id] = new Instructor
            {
                Id = id, Name = s.Name, Neighborhood = hood.Name, Categories = s.Categories,
                PricePerLesson = s.Price, ServiceRadiusKm = s.Radius, AverageRating = s.Rating,
                ReviewCount = s.ReviewCount, LessonsGiven = s.Lessons, Vehicle = s.Vehicle,
                HasDualControl = s.Vehicle.Contains("duplo comando") || s.Categories.HasFlag(LicenseCategory.A),
                IsVerified = s.Verified, MeetingPoint = s.MeetingPoint, Bio = s.Bio,
                DetranCredential = s.Verified ? $"SP-{482000 + id * 137}" : "",
                Latitude = hood.Location.Latitude + (rnd.NextDouble() - 0.5) * 0.008,
                Longitude = hood.Location.Longitude + (rnd.NextDouble() - 0.5) * 0.008,
            };
        }

        SeedSlots(store, now);
        SeedReviews(store, now);
        SeedDemoBookings(store, now);
        store.SyncSequences();
        return store;
    }

    private static void SeedSlots(InMemoryStore store, DateTime now)
    {
        var slotId = 0;
        foreach (var instructor in store.Instructors.Values.OrderBy(i => i.Id))
        {
            var rnd = new Random(instructor.Id * 104729);
            for (var day = 0; day < DaysAhead; day++)
            {
                var date = now.Date.AddDays(day);
                if (date.DayOfWeek == DayOfWeek.Sunday) continue;
                var lastHour = date.DayOfWeek == DayOfWeek.Saturday ? 12 : 17;
                for (var hour = 8; hour <= lastHour; hour++)
                {
                    var start = date.AddHours(hour);
                    if (start <= now) continue;
                    var roll = rnd.NextDouble();
                    var status = roll < 0.15 ? SlotStatus.Booked   // booked by other (fictional) students
                               : roll < 0.25 ? SlotStatus.Blocked  // instructor unavailable
                               : SlotStatus.Available;
                    slotId++;
                    store.Slots[slotId] = new Slot
                    {
                        Id = slotId, InstructorId = instructor.Id, Start = start,
                        End = start.AddMinutes(instructor.LessonMinutes), Status = status
                    };
                }
            }
        }
    }

    private static void SeedReviews(InMemoryStore store, DateTime now)
    {
        var reviewId = 0;
        foreach (var instructor in store.Instructors.Values.Where(i => i.IsVerified).OrderBy(i => i.Id))
        {
            for (var k = 0; k < 3; k++)
            {
                var r = ReviewPool[(instructor.Id * 3 + k) % ReviewPool.Length];
                reviewId++;
                store.Reviews[reviewId] = new Review
                {
                    Id = reviewId, InstructorId = instructor.Id, StudentId = 100 + reviewId,
                    StudentName = r.Student, Rating = r.Rating, Comment = r.Comment,
                    CreatedAt = now.AddDays(-(k * 9 + instructor.Id % 5 + 2))
                };
            }
        }
    }

    private static void SeedDemoBookings(InMemoryStore store, DateTime now)
    {
        var nextSlotId = store.Slots.Keys.DefaultIfEmpty(0).Max();
        var bookingId = 0;

        Booking AddBooking(Slot slot, BookingStatus status, DateTime createdAt)
        {
            var instructor = store.Instructors[slot.InstructorId];
            var fee = Math.Round(instructor.PricePerLesson * SeedFeePercent / 100m, 2, MidpointRounding.AwayFromZero);
            var booking = new Booking
            {
                Id = ++bookingId, StudentId = DemoStudentId, InstructorId = instructor.Id, SlotId = slot.Id,
                Price = instructor.PricePerLesson, PlatformFee = fee, InstructorAmount = instructor.PricePerLesson - fee,
                Status = status, PaymentMethod = PaymentMethod.Pix, PaymentLabel = "PIX",
                CreatedAt = createdAt, PaidAt = createdAt.AddMinutes(3)
            };
            slot.Status = SlotStatus.Booked;
            slot.BookingId = booking.Id;
            store.Bookings[booking.Id] = booking;
            return booking;
        }

        Slot PastSlot(int instructorId, int daysAgo, int hour)
        {
            var start = now.Date.AddDays(-daysAgo).AddHours(hour);
            var slot = new Slot
            {
                Id = ++nextSlotId, InstructorId = instructorId, Start = start,
                End = start.AddMinutes(store.Instructors[instructorId].LessonMinutes)
            };
            store.Slots[slot.Id] = slot;
            return slot;
        }

        // Completed lesson not yet reviewed (demo of "Avaliar aula").
        AddBooking(PastSlot(1, 3, 10), BookingStatus.Completed, now.AddDays(-5));

        // Completed lesson already reviewed by the demo student.
        var reviewed = AddBooking(PastSlot(3, 8, 14), BookingStatus.Completed, now.AddDays(-10));
        var reviewId = store.Reviews.Keys.DefaultIfEmpty(0).Max() + 1;
        store.Reviews[reviewId] = new Review
        {
            Id = reviewId, InstructorId = 3, StudentId = DemoStudentId, StudentName = "Aluno Demo",
            BookingId = reviewed.Id, Rating = 5, Comment = "Primeira aula de moto, me senti muito seguro.",
            CreatedAt = now.AddDays(-8).AddHours(16)
        };

        // Upcoming confirmed lesson so "Próximas" is not empty.
        var upcoming = store.Slots.Values
            .Where(s => s.InstructorId == 2 && s.Status == SlotStatus.Available && s.Start >= now.Date.AddDays(2))
            .OrderBy(s => s.Start).FirstOrDefault();
        if (upcoming is not null) AddBooking(upcoming, BookingStatus.Confirmed, now.AddDays(-1));
    }
}
