using System.Text;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Osteo.Agenda.Core.Domain;

namespace Osteo.Agenda.Core.Data;

/// <summary>
/// The schema is owned by the SQL files in Data/Sql (see <see cref="SqlMigrator"/>);
/// this context only maps onto it, using snake_case names.
/// </summary>
public sealed class AgendaDbContext(DbContextOptions<AgendaDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Staff> Staff => Set<Staff>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<WorkingHours> WorkingHours => Set<WorkingHours>();
    public DbSet<TimeOff> TimeOff => Set<TimeOff>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppointmentEvent> AppointmentEvents => Set<AppointmentEvent>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Location>().ToTable("locations");
        b.Entity<Staff>().ToTable("staff");
        b.Entity<Service>().ToTable("services");
        b.Entity<WorkingHours>().ToTable("working_hours");
        b.Entity<TimeOff>().ToTable("time_off");
        b.Entity<Contact>().ToTable("contacts");
        b.Entity<Patient>().ToTable("patients");
        b.Entity<Appointment>().ToTable("appointments");
        b.Entity<AppointmentEvent>().ToTable("appointment_events");
        b.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        b.Entity<Contact>()
            .HasMany(c => c.Patients)
            .WithOne(p => p.Contact)
            .HasForeignKey(p => p.ContactId);

        b.Entity<Appointment>(e =>
        {
            e.HasOne(a => a.Contact).WithMany().HasForeignKey(a => a.ContactId);
            e.HasOne(a => a.Patient).WithMany().HasForeignKey(a => a.PatientId);
            e.HasOne(a => a.Location).WithMany().HasForeignKey(a => a.LocationId);
            e.HasOne(a => a.Service).WithMany().HasForeignKey(a => a.ServiceId);
            e.Property(a => a.Version).IsConcurrencyToken();
        });

        b.Entity<AppointmentEvent>().HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId);
        b.Entity<AppointmentEvent>().Property(x => x.Data).HasColumnType("jsonb");
        b.Entity<Service>().Property(x => x.Price).HasPrecision(10, 2);
        b.Entity<WorkingHours>().Property(x => x.DayOfWeek).HasConversion<int>();

        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));

                // Domain enums are stored as text (readable, guarded by CHECK constraints).
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (type.IsEnum && type.Namespace == typeof(AppointmentStatus).Namespace)
                {
                    property.SetProviderClrType(typeof(string));
                }
            }
        }
    }

    public static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])))
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
