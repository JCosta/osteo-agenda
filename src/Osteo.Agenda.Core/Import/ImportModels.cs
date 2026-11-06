using Osteo.Agenda.Core.Domain;

namespace Osteo.Agenda.Core.Import;

public sealed record CalendarEvent(
    string Id,
    string? Summary,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? ColorId);

public interface ICalendarSource
{
    bool IsConfigured { get; }

    Task<IReadOnlyList<CalendarEvent>> ListEventsAsync(
        string calendarId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed record ImportRequest(
    string CalendarId,
    Guid LocationId,
    Guid StaffId,
    DateOnly From,
    DateOnly To);

public enum ImportItemKind { Appointment, Block, Ignored }

public enum ImportAction { Create, Update, Unchanged, Skip }

public enum ImportIssue
{
    // Blocking: the event is skipped.
    MissingPhone,
    InvalidPhone,
    MissingName,
    AllDayIgnored,
    OverlapsAppointment,
    OverlapsBlock,

    // Warnings: imported anyway.
    SamePhoneDifferentName,
    Landline,
    ForeignNumber,
}

public static class ImportIssueInfo
{
    public static bool IsBlocking(this ImportIssue issue) => issue switch
    {
        ImportIssue.MissingPhone or ImportIssue.InvalidPhone or ImportIssue.MissingName
            or ImportIssue.AllDayIgnored or ImportIssue.OverlapsAppointment or ImportIssue.OverlapsBlock => true,
        _ => false,
    };

    public static string Label(this ImportIssue issue) => issue switch
    {
        ImportIssue.MissingPhone => "Sem telefone no título",
        ImportIssue.InvalidPhone => "Telefone inválido",
        ImportIssue.MissingName => "Sem nome no título",
        ImportIssue.AllDayIgnored => "Evento de dia inteiro (ignorado)",
        ImportIssue.OverlapsAppointment => "Sobrepõe outra marcação",
        ImportIssue.OverlapsBlock => "Sobrepõe um bloqueio de agenda",
        ImportIssue.SamePhoneDifferentName => "Mesmo telefone com outro nome — fica como paciente distinto do mesmo contacto",
        ImportIssue.Landline => "Telefone fixo — sem WhatsApp/SMS (avisos por email)",
        ImportIssue.ForeignNumber => "Número estrangeiro — confirmar canal",
        _ => issue.ToString(),
    };
}

public sealed class ImportItem
{
    public required CalendarEvent Event { get; init; }
    public required ParsedTitle Parsed { get; init; }
    public ImportItemKind Kind { get; set; }
    public ImportAction Action { get; set; }
    public AppointmentStatus? Status { get; set; }
    public TimeOffKind? BlockKind { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public Guid? ExistingId { get; set; }
    public List<ImportIssue> Issues { get; } = [];

    public bool IsImportable => Action is ImportAction.Create or ImportAction.Update;
}

public sealed record ImportPreview(IReadOnlyList<ImportItem> Items, int NewContacts)
{
    public int Count(ImportAction action) => Items.Count(i => i.Action == action);
    public int Appointments => Items.Count(i => i.Kind == ImportItemKind.Appointment && i.Action != ImportAction.Skip);
    public int Blocks => Items.Count(i => i.Kind == ImportItemKind.Block && i.Action != ImportAction.Skip);
}

public sealed record ImportResult(
    int AppointmentsCreated,
    int AppointmentsUpdated,
    int BlocksCreated,
    int BlocksUpdated,
    int Unchanged,
    int Skipped,
    int ContactsCreated,
    int PatientsCreated);

// Snapshot of what already exists in the database, so the planner stays pure and testable.
public sealed record ExistingAppointment(Guid Id, DateTime StartsAt, DateTime EndsAt, AppointmentStatus Status);
public sealed record ExistingBlock(Guid Id, DateTime StartsAt, DateTime EndsAt);
public sealed record BusyInterval(DateTime StartsAt, DateTime EndsAt);

public sealed record ImportState(
    IReadOnlyDictionary<string, ExistingAppointment> AppointmentsByEventId,
    IReadOnlyDictionary<string, ExistingBlock> BlocksByEventId,
    IReadOnlyList<BusyInterval> OtherActiveAppointments,
    IReadOnlyList<BusyInterval> OtherBlocks,
    IReadOnlySet<string> KnownPhones);
