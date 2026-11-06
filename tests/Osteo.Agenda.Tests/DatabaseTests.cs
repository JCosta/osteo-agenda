using Microsoft.EntityFrameworkCore;
using Npgsql;
using Osteo.Agenda.Core.Contacts;
using Osteo.Agenda.Core.Data;
using Osteo.Agenda.Core.Domain;
using Osteo.Agenda.Core.Import;

namespace Osteo.Agenda.Tests;

/// <summary>
/// Runs against a real PostgreSQL. Set OSTEO_TEST_DB to a server connection string
/// (e.g. "Host=localhost;Username=postgres;Password=postgres"); each test gets its own database.
/// Without the variable these tests are no-ops.
/// </summary>
public sealed class DatabaseTests : IAsyncLifetime
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("OSTEO_TEST_DB");
    private readonly string _dbName = "osteo_test_" + Guid.NewGuid().ToString("N")[..12];
    private string _cs = "";

    private static readonly DateTime Now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        if (Server is null)
        {
            return;
        }
        await using (var conn = new NpgsqlConnection(Server))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {_dbName}", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        _cs = new NpgsqlConnectionStringBuilder(Server) { Database = _dbName }.ConnectionString;
        await SqlMigrator.MigrateAsync(_cs);
    }

    public async Task DisposeAsync()
    {
        if (Server is null)
        {
            return;
        }
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(Server);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_dbName} WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private AgendaDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AgendaDbContext>().UseNpgsql(_cs).Options);

    private async Task<(Guid Staff, Guid Location)> SeedAsync()
    {
        await using var db = NewContext();
        var staff = new Staff { Name = "Joana" };
        var location = new Location { Name = "Braga", GoogleCalendarId = "cal-braga" };
        db.AddRange(staff, location);
        await db.SaveChangesAsync();
        return (staff.Id, location.Id);
    }

    private sealed class FakeSource(params CalendarEvent[] events) : ICalendarSource
    {
        public List<CalendarEvent> Events { get; } = [.. events];
        public bool IsConfigured => true;
        public Task<IReadOnlyList<CalendarEvent>> ListEventsAsync(string calendarId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CalendarEvent>>(Events.Where(e => e.Start < to && e.End > from).ToList());
    }

    private static CalendarEvent Ev(string id, string title, DateTime startUtc, int minutes = 60) =>
        new(id, title, new DateTimeOffset(startUtc, TimeSpan.Zero), new DateTimeOffset(startUtc.AddMinutes(minutes), TimeSpan.Zero), false, null);

    private static DateTime At(int days, int hour) =>
        DateTime.SpecifyKind(Now.Date.AddDays(days).AddHours(hour), DateTimeKind.Utc);

    [Fact]
    public async Task Database_rejects_overlapping_active_appointments()
    {
        if (Server is null) return;
        var (staffId, locationId) = await SeedAsync();

        await using var db = NewContext();
        var contact = new Contact { Name = "Ana", PhoneE164 = "+351912345678" };
        var patient = new Patient { ContactId = contact.Id, Name = "Ana" };
        db.AddRange(contact, patient);
        Appointment Make(DateTime start, AppointmentStatus status) => new()
        {
            StaffId = staffId, LocationId = locationId, ContactId = contact.Id, PatientId = patient.Id,
            StartsAt = start, EndsAt = start.AddHours(1), Status = status, Source = AppointmentSource.App,
            PublicToken = PublicToken.New(),
        };
        db.Add(Make(At(2, 10), AppointmentStatus.Booked));
        await db.SaveChangesAsync();

        // A cancelled one in the same slot is fine…
        db.Add(Make(At(2, 10), AppointmentStatus.Cancelled));
        await db.SaveChangesAsync();

        // …an active overlapping one is not.
        db.Add(Make(At(2, 10).AddMinutes(30), AppointmentStatus.Confirmed));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ExclusionViolation, (ex.InnerException as PostgresException)?.SqlState);
    }

    [Fact]
    public async Task Database_rejects_appointments_inside_a_block()
    {
        if (Server is null) return;
        var (staffId, locationId) = await SeedAsync();

        await using var db = NewContext();
        db.TimeOff.Add(new TimeOff
        {
            StaffId = staffId, LocationId = null, StartsAt = At(3, 8), EndsAt = At(3, 13),
            Kind = TimeOffKind.Morning, Source = TimeOffSource.App,
        });
        var contact = new Contact { Name = "Rui", PhoneE164 = "+351913345678" };
        var patient = new Patient { ContactId = contact.Id, Name = "Rui" };
        db.AddRange(contact, patient);
        await db.SaveChangesAsync();

        db.Add(new Appointment
        {
            StaffId = staffId, LocationId = locationId, ContactId = contact.Id, PatientId = patient.Id,
            StartsAt = At(3, 11), EndsAt = At(3, 12), Status = AppointmentStatus.Booked, Source = AppointmentSource.App,
            PublicToken = PublicToken.New(),
        });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ExclusionViolation, (ex.InnerException as PostgresException)?.SqlState);
    }

    [Fact]
    public async Task Import_creates_history_and_future_then_is_idempotent()
    {
        if (Server is null) return;
        var (staffId, locationId) = await SeedAsync();

        var source = new FakeSource(
            Ev("e1", "Maria Silva #912345678", At(-60, 10)),
            Ev("e2", "✅ Maria Silva #912345678", At(-30, 10)),
            Ev("e3", "Maria Silva #912 345 678", At(5, 10)),
            Ev("e4", "Tomás #933000111", At(-10, 15)),       // siblings on the mother's phone
            Ev("e5", "Inês #933000111", At(-10, 16)),
            Ev("e6", "Fixo #253123456", At(7, 9)),
            Ev("e7", "Contabilista", At(6, 9)),               // no phone -> skipped
            Ev("b1", "#bloqueio Formação", At(8, 8), 5 * 60));

        var request = new ImportRequest("cal-braga", locationId, staffId,
            DateOnly.FromDateTime(At(-365, 0)), DateOnly.FromDateTime(At(365, 0)));

        await using (var db = NewContext())
        {
            var importer = new CalendarImportService(db, source, TimeProvider.System);
            var result = await importer.CommitAsync(request);
            Assert.Equal(6, result.AppointmentsCreated);
            Assert.Equal(1, result.BlocksCreated);
            Assert.Equal(3, result.ContactsCreated);
            Assert.Equal(4, result.PatientsCreated);
            Assert.Equal(1, result.Skipped);
        }

        await using (var db = NewContext())
        {
            var maria = await db.Contacts.Include(c => c.Patients).SingleAsync(c => c.PhoneE164 == "+351912345678");
            Assert.Equal(Channel.WhatsApp, maria.PreferredChannel);
            var landline = await db.Contacts.SingleAsync(c => c.PhoneE164 == "+351253123456");
            Assert.Equal(Channel.Email, landline.PreferredChannel);
            var mother = await db.Contacts.Include(c => c.Patients).SingleAsync(c => c.PhoneE164 == "+351933000111");
            Assert.Equal(2, mother.Patients.Count);

            var profile = await new ContactQueries(db, TimeProvider.System).GetProfileAsync(maria.Id);
            Assert.NotNull(profile);
            Assert.Single(profile.Upcoming);
            Assert.Equal(2, profile.History.Count);
            Assert.Equal(2, profile.Summary.Completed);
            Assert.Equal(At(-30, 10), profile.Summary.LastVisit);

            var queries = new ContactQueries(db, TimeProvider.System);
            Assert.Equal(mother.Id, Assert.Single(await queries.SearchAsync("Inês")).Id);
            Assert.Equal(mother.Id, Assert.Single(await queries.SearchAsync("ines")).Id); // sem acento
            Assert.Equal(mother.Id, Assert.Single(await queries.SearchAsync("TOMAS")).Id);
            Assert.Equal(maria.Id, Assert.Single(await queries.SearchAsync("912 345")).Id);
        }

        // Second run: nothing new.
        await using (var db = NewContext())
        {
            var result = await new CalendarImportService(db, source, TimeProvider.System).CommitAsync(request);
            Assert.Equal(0, result.AppointmentsCreated);
            Assert.Equal(0, result.ContactsCreated);
            Assert.Equal(0, result.AppointmentsUpdated);
            Assert.Equal(7, result.Unchanged);
        }

        // Moved in the calendar -> updated, not duplicated.
        source.Events[2] = Ev("e3", "Maria Silva #912345678", At(5, 14));
        await using (var db = NewContext())
        {
            var result = await new CalendarImportService(db, source, TimeProvider.System).CommitAsync(request);
            Assert.Equal(1, result.AppointmentsUpdated);
            Assert.Equal(0, result.AppointmentsCreated);
            Assert.Equal(6, await db.Appointments.CountAsync());
            var moved = await db.Appointments.SingleAsync(a => a.ExternalEventId == "e3");
            Assert.Equal(At(5, 14), moved.StartsAt);
            Assert.Equal(2, moved.Version);
        }
    }
}
