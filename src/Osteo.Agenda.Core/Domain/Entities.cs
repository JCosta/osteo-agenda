namespace Osteo.Agenda.Core.Domain;

// All DateTime values are UTC (mapped to timestamptz). Local display uses ClinicTime.

public sealed class Location
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? GoogleCalendarId { get; set; }
    /// <summary>Boundary between "manhã" and "tarde" for quick blocks.</summary>
    public TimeOnly MorningEnd { get; set; } = new(13, 0);
    public bool Active { get; set; } = true;
}

public sealed class Staff
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Email { get; set; }
    public string? TelegramChatId { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class Service
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public int DurationMinutes { get; set; }
    public int BufferBeforeMinutes { get; set; }
    public int BufferAfterMinutes { get; set; }
    public decimal? Price { get; set; }
    public bool BookableOnline { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class WorkingHours
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StaffId { get; set; }
    public Guid LocationId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}

public sealed class TimeOff
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StaffId { get; set; }
    /// <summary>Null = all locations.</summary>
    public Guid? LocationId { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public TimeOffKind Kind { get; set; }
    public string? Reason { get; set; }
    public TimeOffSource Source { get; set; }
    public string? ExternalCalendarId { get; set; }
    public string? ExternalEventId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Contact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? PhoneE164 { get; set; }
    public PhoneType PhoneType { get; set; }
    public string? Email { get; set; }
    public Channel PreferredChannel { get; set; } = Channel.WhatsApp;
    public DateTime? WhatsappUnavailableSince { get; set; }
    public bool ConsentReminders { get; set; } = true;
    public bool ConsentMarketing { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Patient> Patients { get; set; } = [];
}

public sealed class Patient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContactId { get; set; }
    public Contact? Contact { get; set; }
    public required string Name { get; set; }
    public DateOnly? BirthDate { get; set; }
    public PatientRelationship Relationship { get; set; }
}

public sealed class Appointment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StaffId { get; set; }
    public Guid LocationId { get; set; }
    public Location? Location { get; set; }
    public Guid? ServiceId { get; set; }
    public Service? Service { get; set; }
    public Guid PatientId { get; set; }
    public Patient? Patient { get; set; }
    public Guid ContactId { get; set; }
    public Contact? Contact { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public AppointmentStatus Status { get; set; }
    public AppointmentSource Source { get; set; }
    public string? ExternalCalendarId { get; set; }
    public string? ExternalEventId { get; set; }
    public string? InternalNotes { get; set; }
    public required string PublicToken { get; set; }
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AppointmentEvent
{
    public long Id { get; set; }
    public Guid AppointmentId { get; set; }
    public required string Type { get; set; }
    /// <summary>JSON payload.</summary>
    public string? Data { get; set; }
    public required string Actor { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
