using System.Linq.Expressions;
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
    private static readonly Expression<Func<Appointment, AppointmentRow>> ToRow = a => new AppointmentRow(
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
        a.Contact!.Name);

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
        // Anonymous projection (translatable ordering), then map to the record in memory.
        var rows = await query
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.PhoneE164,
                c.PreferredChannel,
                Next = db.Appointments
                    .Where(a => a.ContactId == c.Id && a.StartsAt >= now && active.Contains(a.Status))
                    .Min(a => (DateTime?)a.StartsAt),
                Last = db.Appointments
                    .Where(a => a.ContactId == c.Id && a.StartsAt < now && a.Status == AppointmentStatus.Completed)
                    .Max(a => (DateTime?)a.StartsAt),
            })
            .OrderBy(c => c.Next == null)
            .ThenBy(c => c.Next)
            .ThenByDescending(c => c.Last)
            .ThenBy(c => c.Name)
            .Take(limit)
            .ToListAsync(ct);

        return rows
            .Select(c => new ContactListItem(c.Id, c.Name, c.PhoneE164, c.PreferredChannel, c.Next, c.Last))
            .ToList();
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

        var active = DomainLabels.ActiveStatuses;
        var all = db.Appointments.AsNoTracking().Where(a => a.ContactId == contactId);

        // Filter on the entity first, then project: EF cannot translate filters on the projected record.
        var upcoming = await all
            .Where(a => a.StartsAt >= now && active.Contains(a.Status))
            .OrderBy(a => a.StartsAt)
            .Select(ToRow)
            .ToListAsync(ct);

        var historyQuery = all.Where(a => a.StartsAt < now || !active.Contains(a.Status));
        var historyTotal = await historyQuery.CountAsync(ct);
        var history = await historyQuery
            .OrderByDescending(a => a.StartsAt)
            .Take(historyLimit)
            .Select(ToRow)
            .ToListAsync(ct);

        var completed = all.Where(a => a.Status == AppointmentStatus.Completed);
        var summary = new ContactSummary(
            Completed: await completed.CountAsync(ct),
            NoShows: await all.CountAsync(a => a.Status == AppointmentStatus.NoShow, ct),
            Cancelled: await all.CountAsync(a => a.Status == AppointmentStatus.Cancelled, ct),
            FirstVisit: await completed.MinAsync(a => (DateTime?)a.StartsAt, ct),
            LastVisit: await completed.MaxAsync(a => (DateTime?)a.StartsAt, ct));

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
            .Select(ToRow)
            .ToListAsync(ct);
    }
}
