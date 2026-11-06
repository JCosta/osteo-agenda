using Microsoft.EntityFrameworkCore;
using Osteo.Agenda.Core.Data;
using Osteo.Agenda.Core.Domain;

namespace Osteo.Agenda.Core.Contacts;

public sealed record ContactListItem(Guid Id, string Name, string? PhoneE164, Channel PreferredChannel, DateTime? NextAppointment, DateTime? LastAppointment);

public sealed record AppointmentRow(
    Guid Id,
    DateTime StartsAt,
    DateTime EndsAt,
    AppointmentStatus Status,
    string PatientName,
    string LocationName,
    string? ServiceName,
    AppointmentSource Source,
    string? InternalNotes,
    Guid ContactId,
    string ContactName);

public sealed record ContactSummary(
    int Completed,
    int NoShows,
    int Cancelled,
    DateTime? FirstVisit,
    DateTime? LastVisit);

public sealed record ContactProfile(
    Contact Contact,
    IReadOnlyList<Patient> Patients,
    IReadOnlyList<AppointmentRow> Upcoming,
    IReadOnlyList<AppointmentRow> History,
    int HistoryTotal,
    ContactSummary Summary);

public sealed class ContactQueries(AgendaDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<ContactListItem>> SearchAsync(string? term, int limit = 50, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var query = db.Contacts.AsNoTracking();

        term = term?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var digits = new string(term.Where(char.IsAsciiDigit).ToArray());
            if (digits.Length >= 3)
            {
                query = query.Where(c => c.PhoneE164 != null && c.PhoneE164.Contains(digits));
            }
            else
            {
                var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                query = query.Where(c => EF.Functions.ILike(c.Name, pattern)
                    || c.Patients.Any(p => EF.Functions.ILike(p.Name, pattern)));
            }
        }

        var active = DomainLabels.ActiveStatuses;
        return await query
            .Select(c => new ContactListItem(
                c.Id,
                c.Name,
                c.PhoneE164,
                c.PreferredChannel,
                db.Appointments.Where(a => a.ContactId == c.Id && a.StartsAt >= now && active.Contains(a.Status))
                    .Min(a => (DateTime?)a.StartsAt),
                db.Appointments.Where(a => a.ContactId == c.Id && a.StartsAt < now && a.Status == AppointmentStatus.Completed)
                    .Max(a => (DateTime?)a.StartsAt)))
            .OrderBy(c => c.NextAppointment == null)
            .ThenBy(c => c.NextAppointment)
            .ThenByDescending(c => c.LastAppointment)
            .ThenBy(c => c.Name)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<ContactProfile?> GetProfileAsync(Guid contactId, int historyLimit = 10, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var contact = await db.Contacts.AsNoTracking()
            .Include(c => c.Patients)
            .SingleOrDefaultAsync(c => c.Id == contactId, ct);
        if (contact is null)
        {
            return null;
        }

        var rows = db.Appointments.AsNoTracking()
            .Where(a => a.ContactId == contactId)
            .Select(a => new AppointmentRow(
                a.Id,
                a.StartsAt,
                a.EndsAt,
                a.Status,
                a.Patient!.Name,
                a.Location!.Name,
                a.Service != null ? a.Service.Name : null,
                a.Source,
                a.InternalNotes,
                a.ContactId,
                a.Contact!.Name));

        var active = DomainLabels.ActiveStatuses;
        var upcoming = await rows
            .Where(a => a.StartsAt >= now && active.Contains(a.Status))
            .OrderBy(a => a.StartsAt)
            .ToListAsync(ct);

        var historyQuery = rows.Where(a => a.StartsAt < now || !active.Contains(a.Status));
        var historyTotal = await historyQuery.CountAsync(ct);
        var history = await historyQuery
            .OrderByDescending(a => a.StartsAt)
            .Take(historyLimit)
            .ToListAsync(ct);

        var stats = await db.Appointments.AsNoTracking()
            .Where(a => a.ContactId == contactId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Completed = g.Count(a => a.Status == AppointmentStatus.Completed),
                NoShows = g.Count(a => a.Status == AppointmentStatus.NoShow),
                Cancelled = g.Count(a => a.Status == AppointmentStatus.Cancelled),
                First = g.Where(a => a.Status == AppointmentStatus.Completed).Min(a => (DateTime?)a.StartsAt),
                Last = g.Where(a => a.Status == AppointmentStatus.Completed).Max(a => (DateTime?)a.StartsAt),
            })
            .SingleOrDefaultAsync(ct);

        var summary = stats is null
            ? new ContactSummary(0, 0, 0, null, null)
            : new ContactSummary(stats.Completed, stats.NoShows, stats.Cancelled, stats.First, stats.Last);

        return new ContactProfile(
            contact,
            contact.Patients.OrderBy(p => p.Name).ToList(),
            upcoming,
            history,
            historyTotal,
            summary);
    }

    public async Task<IReadOnlyList<AppointmentRow>> DayAsync(DateOnly day, CancellationToken ct = default)
    {
        var from = ClinicTime.ToUtc(day, TimeOnly.MinValue);
        var to = ClinicTime.ToUtc(day.AddDays(1), TimeOnly.MinValue);
        return await db.Appointments.AsNoTracking()
            .Where(a => a.StartsAt >= from && a.StartsAt < to && a.Status != AppointmentStatus.Cancelled)
            .OrderBy(a => a.StartsAt)
            .Select(a => new AppointmentRow(
                a.Id, a.StartsAt, a.EndsAt, a.Status, a.Patient!.Name, a.Location!.Name,
                a.Service != null ? a.Service.Name : null, a.Source, a.InternalNotes, a.ContactId, a.Contact!.Name))
            .ToListAsync(ct);
    }
}
