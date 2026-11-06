using Osteo.Agenda.Core.Domain;

namespace Osteo.Agenda.Core.Import;

/// <summary>
/// Decides, for every calendar event, what the import will do. Pure: no I/O.
/// </summary>
public static class ImportPlanner
{
    // Google Calendar colour ids used by the Apps Script: 10 = Basil (verde), 2 = Sage, 6 = Tangerine (laranja).
    private static readonly HashSet<string> ConfirmedColors = ["10", "2"];
    private static readonly HashSet<string> RescheduleColors = ["6"];

    public static ImportPreview Plan(IReadOnlyList<CalendarEvent> events, ImportState state, DateTime nowUtc)
    {
        var items = events
            .OrderBy(e => e.Start)
            .Select(e => Classify(e, state, nowUtc))
            .ToList();

        FlagOverlaps(items, state);
        FlagNames(items);

        var newPhones = items
            .Where(i => i.Kind == ImportItemKind.Appointment && i.IsImportable && i.Parsed.Phone is not null)
            .Select(i => i.Parsed.Phone!.Value.E164)
            .Where(p => !state.KnownPhones.Contains(p))
            .Distinct()
            .Count();

        return new ImportPreview(items, newPhones);
    }

    private static ImportItem Classify(CalendarEvent ev, ImportState state, DateTime nowUtc)
    {
        var parsed = TitleParser.Parse(ev.Summary);
        var item = new ImportItem
        {
            Event = ev,
            Parsed = parsed,
            StartsAtUtc = ev.Start.UtcDateTime,
            EndsAtUtc = ev.End.UtcDateTime,
        };

        if (parsed.IsBlock)
        {
            item.Kind = ImportItemKind.Block;
            item.BlockKind = ev.IsAllDay ? TimeOffKind.FullDay : TimeOffKind.Custom;
            if (state.BlocksByEventId.TryGetValue(ev.Id, out var existing))
            {
                item.ExistingId = existing.Id;
                item.Action = existing.StartsAt == item.StartsAtUtc && existing.EndsAt == item.EndsAtUtc
                    ? ImportAction.Unchanged
                    : ImportAction.Update;
            }
            else
            {
                item.Action = ImportAction.Create;
            }
            return item;
        }

        item.Kind = ImportItemKind.Appointment;

        if (ev.IsAllDay)
        {
            item.Kind = ImportItemKind.Ignored;
            Skip(item, ImportIssue.AllDayIgnored);
            return item;
        }
        if (parsed.RawPhone is null)
        {
            Skip(item, ImportIssue.MissingPhone);
            return item;
        }
        if (parsed.Phone is null)
        {
            Skip(item, ImportIssue.InvalidPhone);
            return item;
        }
        if (parsed.Name.Length == 0)
        {
            Skip(item, ImportIssue.MissingName);
            return item;
        }

        if (parsed.Phone.Value.Type == PhoneType.Landline)
        {
            item.Issues.Add(ImportIssue.Landline);
        }
        else if (!parsed.Phone.Value.E164.StartsWith("+351", StringComparison.Ordinal))
        {
            item.Issues.Add(ImportIssue.ForeignNumber);
        }

        item.Status = StatusFor(ev, parsed, nowUtc);

        if (state.AppointmentsByEventId.TryGetValue(ev.Id, out var current))
        {
            item.ExistingId = current.Id;
            if (!CalendarMayOverride(current.Status))
            {
                item.Status = current.Status;
            }
            var same = current.StartsAt == item.StartsAtUtc
                && current.EndsAt == item.EndsAtUtc
                && current.Status == item.Status;
            item.Action = same ? ImportAction.Unchanged : ImportAction.Update;
        }
        else
        {
            item.Action = ImportAction.Create;
        }

        return item;
    }

    /// <summary>Decisions taken in the app (cancel, no-show, awaiting reply) are not overwritten by the calendar.</summary>
    public static bool CalendarMayOverride(AppointmentStatus current) => current is
        AppointmentStatus.Booked or AppointmentStatus.Confirmed or
        AppointmentStatus.RescheduleRequested or AppointmentStatus.Completed;

    private static AppointmentStatus StatusFor(CalendarEvent ev, ParsedTitle parsed, DateTime nowUtc)
    {
        if (ev.End.UtcDateTime <= nowUtc)
        {
            return AppointmentStatus.Completed;
        }
        if (parsed.ConfirmedMark || (ev.ColorId is not null && ConfirmedColors.Contains(ev.ColorId)))
        {
            return AppointmentStatus.Confirmed;
        }
        if (ev.ColorId is not null && RescheduleColors.Contains(ev.ColorId))
        {
            return AppointmentStatus.RescheduleRequested;
        }
        return AppointmentStatus.Booked;
    }

    private static void FlagOverlaps(List<ImportItem> items, ImportState state)
    {
        var blocks = state.OtherBlocks.ToList();
        blocks.AddRange(items
            .Where(i => i.Kind == ImportItemKind.Block && i.Action != ImportAction.Skip)
            .Select(i => new BusyInterval(i.StartsAtUtc, i.EndsAtUtc)));

        var busy = state.OtherActiveAppointments.ToList();

        foreach (var item in items.Where(i => i.Kind == ImportItemKind.Appointment && i.Action != ImportAction.Skip))
        {
            if (item.Status is not { } status || !status.IsActive())
            {
                continue; // past/completed appointments don't occupy the agenda
            }
            if (blocks.Any(b => Overlaps(b, item)))
            {
                Skip(item, ImportIssue.OverlapsBlock);
                continue;
            }
            if (busy.Any(b => Overlaps(b, item)))
            {
                Skip(item, ImportIssue.OverlapsAppointment);
                continue;
            }
            busy.Add(new BusyInterval(item.StartsAtUtc, item.EndsAtUtc));
        }
    }

    private static void FlagNames(List<ImportItem> items)
    {
        var byPhone = items
            .Where(i => i.Kind == ImportItemKind.Appointment && i.Action != ImportAction.Skip && i.Parsed.Phone is not null)
            .GroupBy(i => i.Parsed.Phone!.Value.E164);

        foreach (var group in byPhone)
        {
            var names = group.Select(i => NameKey(i.Parsed.Name)).Distinct().Count();
            if (names <= 1)
            {
                continue;
            }
            foreach (var item in group)
            {
                item.Issues.Add(ImportIssue.SamePhoneDifferentName);
            }
        }
    }

    public static string NameKey(string name) => name.Trim().ToLowerInvariant();

    private static bool Overlaps(BusyInterval b, ImportItem i) => b.StartsAt < i.EndsAtUtc && i.StartsAtUtc < b.EndsAt;

    private static void Skip(ImportItem item, ImportIssue issue)
    {
        item.Action = ImportAction.Skip;
        item.Issues.Add(issue);
    }
}
