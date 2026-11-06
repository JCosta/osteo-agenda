using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Osteo.Agenda.Core.Data;
using Osteo.Agenda.Core.Domain;

namespace Osteo.Agenda.Core.Setup;

public sealed class ClinicOptions
{
    public string StaffName { get; set; } = "Joana Teixeira";
    public string? StaffEmail { get; set; }
    public List<LocationOptions> Locations { get; set; } = [];
}

public sealed class LocationOptions
{
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? GoogleCalendarId { get; set; }
    public string? MorningEnd { get; set; }
}

/// <summary>Creates the professional and locations from configuration (idempotent; matched by name).</summary>
public sealed class ClinicSeeder(AgendaDbContext db, IOptions<ClinicOptions> options)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var o = options.Value;

        if (!await db.Staff.AnyAsync(s => s.Name == o.StaffName, ct))
        {
            db.Staff.Add(new Staff { Name = o.StaffName, Email = o.StaffEmail });
        }

        foreach (var l in o.Locations.Where(l => !string.IsNullOrWhiteSpace(l.Name)))
        {
            var location = await db.Locations.SingleOrDefaultAsync(x => x.Name == l.Name, ct);
            if (location is null)
            {
                location = new Location { Name = l.Name };
                db.Locations.Add(location);
            }
            location.Address = l.Address ?? location.Address;
            location.GoogleCalendarId = l.GoogleCalendarId ?? location.GoogleCalendarId;
            if (TimeOnly.TryParse(l.MorningEnd, out var morningEnd))
            {
                location.MorningEnd = morningEnd;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
