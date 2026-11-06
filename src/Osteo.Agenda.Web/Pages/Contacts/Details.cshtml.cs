using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Osteo.Agenda.Core.Contacts;

namespace Osteo.Agenda.Web.Pages.Contacts;

public sealed class DetailsModel(ContactQueries queries, TimeProvider clock) : PageModel
{
    public const int DefaultHistory = 10;

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool All { get; set; }

    public ContactProfile Profile { get; private set; } = null!;
    public DateTime NowUtc { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        NowUtc = clock.GetUtcNow().UtcDateTime;
        var profile = await queries.GetProfileAsync(Id, All ? 1000 : DefaultHistory, ct);
        if (profile is null)
        {
            return NotFound();
        }
        Profile = profile;
        return Page();
    }
}
