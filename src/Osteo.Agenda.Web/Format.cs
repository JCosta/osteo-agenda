using System.Globalization;
using Osteo.Agenda.Core;
using Osteo.Agenda.Core.Domain;

namespace Osteo.Agenda.Web;

public static class Format
{
    public static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-PT");

    public static string Date(DateTime utc) => ClinicTime.ToLocal(utc).ToString("ddd, d MMM yyyy", Pt);
    public static string DateShort(DateTime utc) => ClinicTime.ToLocal(utc).ToString("dd/MM/yyyy", Pt);
    public static string Time(DateTime utc) => ClinicTime.ToLocal(utc).ToString("HH:mm", Pt);
    public static string DateAndTime(DateTime utc) => $"{Date(utc)} · {Time(utc)}";
    public static string Range(DateTime startUtc, DateTime endUtc) => $"{Time(startUtc)}–{Time(endUtc)}";

    public static string Ago(DateTime utc, DateTime nowUtc)
    {
        var days = (int)Math.Floor((nowUtc - utc).TotalDays);
        return days switch
        {
            < 1 => "hoje",
            1 => "ontem",
            < 14 => $"há {days} dias",
            < 60 => $"há {days / 7} semanas",
            < 730 => $"há {days / 30} meses",
            _ => $"há {days / 365} anos",
        };
    }

    public static string StatusClass(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Confirmed => "text-bg-success",
        AppointmentStatus.Booked or AppointmentStatus.AwaitingConfirmation => "text-bg-primary",
        AppointmentStatus.RescheduleRequested => "text-bg-warning",
        AppointmentStatus.Completed => "text-bg-secondary",
        AppointmentStatus.NoShow => "text-bg-danger",
        AppointmentStatus.Cancelled => "text-bg-light border",
        _ => "text-bg-light",
    };
}
