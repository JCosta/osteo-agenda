using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Osteo.Agenda.Core;
using Osteo.Agenda.Core.Contacts;

namespace Osteo.Agenda.Web.Pages;

public sealed class IndexModel(ContactQueries queries, TimeProvider clock) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public DateOnly? Day { get; set; }

    public DateOnly CurrentDay { get; private set; }
    public bool IsToday { get; private set; }
    public IReadOnlyList<AppointmentRow> Appointments { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        var today = ClinicTime.Today(clock);
        CurrentDay = Day ?? today;
        IsToday = CurrentDay == today;
        Appointments = await queries.DayAsync(CurrentDay, ct);
    }
}
