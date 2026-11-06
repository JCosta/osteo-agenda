using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Osteo.Agenda.Core.Data;
using Osteo.Agenda.Core.Domain;

namespace Osteo.Agenda.Core.Import;

public sealed class ImportConflictException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// Imports appointments (past and future) and "#bloqueio" blocks from a Google Calendar.
/// Re-running is safe: rows are matched by the Google event id and updated, never duplicated.
/// No notifications are created by an import.
/// </summary>
public sealed class CalendarImportService(AgendaDbContext db, ICalendarSource source, TimeProvider clock)
{
    public const string Actor = "importação";

    public async Task<ImportPreview> PreviewAsync(ImportRequest request, CancellationToken ct = default)
    {
        var (events, state) = await LoadAsync(request, ct);
        return ImportPlanner.Plan(events, state, clock.GetUtcNow().UtcDateTime);
    }

    public async Task<ImportResult> CommitAsync(ImportRequest request, CancellationToken ct = default)
    {
        var (events, state) = await LoadAsync(request, ct);
        var preview = ImportPlanner.Plan(events, state, clock.GetUtcNow().UtcDateTime);
        var now = clock.GetUtcNow().UtcDateTime;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        int blocksCreated = 0, blocksUpdated = 0, apptCreated = 0, apptUpdated = 0, contactsCreated = 0, patientsCreated = 0;

        // 1. Blocks first, so the appointment trigger sees them.
        foreach (var item in preview.Items.Where(i => i.Kind == ImportItemKind.Block && i.IsImportable))
        {
            if (item.Action == ImportAction.Create)
            {
                db.TimeOff.Add(new TimeOff
                {
                    StaffId = request.StaffId,
                    LocationId = request.LocationId,
                    StartsAt = item.StartsAtUtc,
                    EndsAt = item.EndsAtUtc,
                    Kind = item.BlockKind ?? TimeOffKind.Custom,
                    Reason = BlockReason(item.Event.Summary),
                    Source = TimeOffSource.Calendar,
                    ExternalCalendarId = request.CalendarId,
                    ExternalEventId = item.Event.Id,
                });
                blocksCreated++;
            }
            else
            {
                var block = await db.TimeOff.SingleAsync(t => t.Id == item.ExistingId, ct);
                block.StartsAt = item.StartsAtUtc;
                block.EndsAt = item.EndsAtUtc;
                block.Kind = item.BlockKind ?? block.Kind;
                blocksUpdated++;
            }
        }
        await SaveAsync(ct);

        // 2. Contacts and patients.
        var appointmentItems = preview.Items
            .Where(i => i.Kind == ImportItemKind.Appointment && i.IsImportable)
            .ToList();

        var phones = appointmentItems.Select(i => i.Parsed.Phone!.Value.E164).Distinct().ToList();
        var contacts = await db.Contacts
            .Include(c => c.Patients)
            .Where(c => c.PhoneE164 != null && phones.Contains(c.PhoneE164))
            .ToDictionaryAsync(c => c.PhoneE164!, ct);

        foreach (var group in appointmentItems.GroupBy(i => i.Parsed.Phone!.Value.E164))
        {
            if (contacts.ContainsKey(group.Key))
            {
                var existing = contacts[group.Key];
                // An explicit "#sms" / "#email" tag in the calendar is a deliberate choice: honour it.
                var tagged = group.Select(i => i.Parsed.Channel).LastOrDefault(c => c is not null);
                if (tagged is { } channel)
                {
                    existing.PreferredChannel = channel;
                }
                continue;
            }

            var latest = group.OrderBy(i => i.StartsAtUtc).Last();
            var phone = latest.Parsed.Phone!.Value;
            var contact = new Contact
            {
                Name = latest.Parsed.Name,
                PhoneE164 = phone.E164,
                PhoneType = phone.Type,
                PreferredChannel = group.Select(i => i.Parsed.Channel).LastOrDefault(c => c is not null)
                    ?? (phone.Type == PhoneType.Landline ? Channel.Email : Channel.WhatsApp),
                CreatedAt = now,
            };
            db.Contacts.Add(contact);
            contacts[group.Key] = contact;
            contactsCreated++;
        }

        Patient PatientFor(Contact contact, string name)
        {
            var key = ImportPlanner.NameKey(name);
            var patient = contact.Patients.FirstOrDefault(p => ImportPlanner.NameKey(p.Name) == key);
            if (patient is not null)
            {
                return patient;
            }
            patient = new Patient
            {
                ContactId = contact.Id,
                Name = name,
                Relationship = ImportPlanner.NameKey(contact.Name) == key ? PatientRelationship.Self : PatientRelationship.Other,
            };
            contact.Patients.Add(patient);
            db.Patients.Add(patient);
            patientsCreated++;
            return patient;
        }

        // 3. Appointments.
        foreach (var item in appointmentItems)
        {
            var contact = contacts[item.Parsed.Phone!.Value.E164];
            var patient = PatientFor(contact, item.Parsed.Name);

            if (item.Action == ImportAction.Create)
            {
                var appointment = new Appointment
                {
                    StaffId = request.StaffId,
                    LocationId = request.LocationId,
                    PatientId = patient.Id,
                    ContactId = contact.Id,
                    StartsAt = item.StartsAtUtc,
                    EndsAt = item.EndsAtUtc,
                    Status = item.Status!.Value,
                    Source = AppointmentSource.Import,
                    ExternalCalendarId = request.CalendarId,
                    ExternalEventId = item.Event.Id,
                    InternalNotes = item.Parsed.ServiceHint is { } hint ? $"Serviço no calendário: {hint}" : null,
                    PublicToken = PublicToken.New(),
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.Appointments.Add(appointment);
                db.AppointmentEvents.Add(new AppointmentEvent
                {
                    AppointmentId = appointment.Id,
                    Type = "imported",
                    Actor = Actor,
                    OccurredAt = now,
                    Data = JsonSerializer.Serialize(new { calendarId = request.CalendarId, eventId = item.Event.Id, title = item.Event.Summary }),
                });
                apptCreated++;
            }
            else
            {
                var appointment = await db.Appointments.SingleAsync(a => a.Id == item.ExistingId, ct);
                var before = new { appointment.StartsAt, appointment.EndsAt, Status = appointment.Status.ToString() };
                appointment.StartsAt = item.StartsAtUtc;
                appointment.EndsAt = item.EndsAtUtc;
                appointment.Status = item.Status!.Value;
                appointment.Version++;
                appointment.UpdatedAt = now;
                db.AppointmentEvents.Add(new AppointmentEvent
                {
                    AppointmentId = appointment.Id,
                    Type = "import_updated",
                    Actor = Actor,
                    OccurredAt = now,
                    Data = JsonSerializer.Serialize(new
                    {
                        before,
                        after = new { appointment.StartsAt, appointment.EndsAt, Status = appointment.Status.ToString() },
                    }),
                });
                apptUpdated++;
            }
        }

        await SaveAsync(ct);
        await tx.CommitAsync(ct);

        return new ImportResult(
            AppointmentsCreated: apptCreated,
            AppointmentsUpdated: apptUpdated,
            BlocksCreated: blocksCreated,
            BlocksUpdated: blocksUpdated,
            Unchanged: preview.Count(ImportAction.Unchanged),
            Skipped: preview.Count(ImportAction.Skip),
            ContactsCreated: contactsCreated,
            PatientsCreated: patientsCreated);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation } pg)
        {
            throw new ImportConflictException(
                "A importação foi cancelada: uma marcação sobrepõe-se a outra ou a um bloqueio que mudou entretanto. " +
                "Volte a pré-visualizar.", pg);
        }
    }

    private static string? BlockReason(string? summary)
    {
        if (summary is null)
        {
            return null;
        }
        var text = summary.Replace("#bloqueio", "", StringComparison.OrdinalIgnoreCase).Trim();
        return text.Length == 0 ? null : text;
    }

    private async Task<(IReadOnlyList<CalendarEvent> Events, ImportState State)> LoadAsync(ImportRequest request, CancellationToken ct)
    {
        if (request.To < request.From)
        {
            throw new ArgumentException("A data final tem de ser igual ou posterior à inicial.");
        }

        var fromUtc = ClinicTime.ToUtc(request.From, TimeOnly.MinValue);
        var toUtc = ClinicTime.ToUtc(request.To.AddDays(1), TimeOnly.MinValue);

        var events = (await source.ListEventsAsync(
                request.CalendarId,
                new DateTimeOffset(fromUtc, TimeSpan.Zero),
                new DateTimeOffset(toUtc, TimeSpan.Zero),
                ct))
            .Where(e => e.End > e.Start)
            .ToList();

        var eventIds = events.Select(e => e.Id).ToList();

        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => a.ExternalCalendarId == request.CalendarId && a.ExternalEventId != null && eventIds.Contains(a.ExternalEventId))
            .Select(a => new { a.ExternalEventId, a.Id, a.StartsAt, a.EndsAt, a.Status })
            .ToListAsync(ct);

        var blocks = await db.TimeOff.AsNoTracking()
            .Where(t => t.ExternalCalendarId == request.CalendarId && t.ExternalEventId != null && eventIds.Contains(t.ExternalEventId))
            .Select(t => new { t.ExternalEventId, t.Id, t.StartsAt, t.EndsAt })
            .ToListAsync(ct);

        var appointmentIds = appointments.Select(a => a.Id).ToList();
        var blockIds = blocks.Select(b => b.Id).ToList();
        var active = DomainLabels.ActiveStatuses;

        var otherActive = await db.Appointments.AsNoTracking()
            .Where(a => a.StaffId == request.StaffId
                && active.Contains(a.Status)
                && a.StartsAt < toUtc && a.EndsAt > fromUtc
                && !appointmentIds.Contains(a.Id))
            .Select(a => new BusyInterval(a.StartsAt, a.EndsAt))
            .ToListAsync(ct);

        var otherBlocks = await db.TimeOff.AsNoTracking()
            .Where(t => t.StaffId == request.StaffId
                && (t.LocationId == null || t.LocationId == request.LocationId)
                && t.StartsAt < toUtc && t.EndsAt > fromUtc
                && !blockIds.Contains(t.Id))
            .Select(t => new BusyInterval(t.StartsAt, t.EndsAt))
            .ToListAsync(ct);

        var phones = events
            .Select(e => TitleParser.Parse(e.Summary).Phone?.E164)
            .Where(p => p is not null)
            .Distinct()
            .ToList();

        var knownPhones = await db.Contacts.AsNoTracking()
            .Where(c => c.PhoneE164 != null && phones.Contains(c.PhoneE164))
            .Select(c => c.PhoneE164!)
            .ToListAsync(ct);

        var state = new ImportState(
            AppointmentsByEventId: appointments.ToDictionary(
                a => a.ExternalEventId!, a => new ExistingAppointment(a.Id, a.StartsAt, a.EndsAt, a.Status)),
            BlocksByEventId: blocks.ToDictionary(
                b => b.ExternalEventId!, b => new ExistingBlock(b.Id, b.StartsAt, b.EndsAt)),
            OtherActiveAppointments: otherActive,
            OtherBlocks: otherBlocks,
            KnownPhones: knownPhones.ToHashSet(StringComparer.Ordinal));

        return (events, state);
    }
}
