namespace Osteo.Agenda.Core.Domain;

public enum AppointmentStatus
{
    Booked,
    AwaitingConfirmation,
    Confirmed,
    RescheduleRequested,
    Completed,
    NoShow,
    Cancelled,
}

public enum AppointmentSource { App, Online, Calendar, Import }

public enum Channel { WhatsApp, Sms, Email }

public enum PhoneType { Unknown, Mobile, Landline }

public enum PatientRelationship { Self, Child, Other }

public enum TimeOffKind { FullDay, Morning, Afternoon, Custom }

public enum TimeOffSource { App, Calendar }

public static class DomainLabels
{
    /// <summary>Statuses that occupy the agenda (enforced by the exclusion constraint in the database).</summary>
    public static readonly AppointmentStatus[] ActiveStatuses =
    [
        AppointmentStatus.Booked,
        AppointmentStatus.AwaitingConfirmation,
        AppointmentStatus.Confirmed,
        AppointmentStatus.RescheduleRequested,
    ];

    public static bool IsActive(this AppointmentStatus status) => ActiveStatuses.Contains(status);

    public static string Label(this AppointmentStatus status) => status switch
    {
        AppointmentStatus.Booked => "Marcada",
        AppointmentStatus.AwaitingConfirmation => "A aguardar confirmação",
        AppointmentStatus.Confirmed => "Confirmada",
        AppointmentStatus.RescheduleRequested => "Pede remarcação",
        AppointmentStatus.Completed => "Realizada",
        AppointmentStatus.NoShow => "Faltou",
        AppointmentStatus.Cancelled => "Cancelada",
        _ => status.ToString(),
    };

    public static string Label(this Channel channel) => channel switch
    {
        Channel.WhatsApp => "WhatsApp",
        Channel.Sms => "SMS",
        Channel.Email => "Email",
        _ => channel.ToString(),
    };

    public static string Label(this PatientRelationship relationship) => relationship switch
    {
        PatientRelationship.Self => "Próprio",
        PatientRelationship.Child => "Filho/a",
        PatientRelationship.Other => "Outro",
        _ => relationship.ToString(),
    };

    public static string Label(this TimeOffKind kind) => kind switch
    {
        TimeOffKind.FullDay => "Dia inteiro",
        TimeOffKind.Morning => "Manhã",
        TimeOffKind.Afternoon => "Tarde",
        TimeOffKind.Custom => "Personalizado",
        _ => kind.ToString(),
    };
}
