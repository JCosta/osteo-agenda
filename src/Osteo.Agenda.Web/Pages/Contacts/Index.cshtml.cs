using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Osteo.Agenda.Core.Contacts;

namespace Osteo.Agenda.Web.Pages.Contacts;

public sealed class IndexModel(ContactQueries queries) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public IReadOnlyList<ContactListItem> Results { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Results = await queries.SearchAsync(Q, 50, ct);
    }
}
