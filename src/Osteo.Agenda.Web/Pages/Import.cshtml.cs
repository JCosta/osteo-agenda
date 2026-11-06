using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Osteo.Agenda.Core;
using Osteo.Agenda.Core.Data;
using Osteo.Agenda.Core.Domain;
using Osteo.Agenda.Core.Import;

namespace Osteo.Agenda.Web.Pages;

public sealed class ImportModel(
    AgendaDbContext db,
    CalendarImportService importer,
    ICalendarSource source,
    TimeProvider clock,
    ILogger<ImportModel> logger) : PageModel
{
    [BindProperty]
    public Guid LocationId { get; set; }

    [BindProperty]
    public string? CalendarId { get; set; }

    [BindProperty]
    public DateOnly From { get; set; }

    [BindProperty]
    public DateOnly To { get; set; }

    public IReadOnlyList<Location> Locations { get; private set; } = [];
    public bool SourceConfigured => source.IsConfigured;
    public ImportPreview? Preview { get; private set; }
    public ImportResult? Result { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadLocationsAsync(ct);
        var today = ClinicTime.Today(clock);
        From = today.AddMonths(-24);
        To = today.AddMonths(12);
        LocationId = Locations.FirstOrDefault()?.Id ?? Guid.Empty;
    }

    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken ct)
    {
        await LoadLocationsAsync(ct);
        var request = await BuildRequestAsync(ct);
        if (request is null)
        {
            return Page();
        }
        try
        {
            Preview = await importer.PreviewAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Import preview failed");
            Error = Describe(ex);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostCommitAsync(CancellationToken ct)
    {
        await LoadLocationsAsync(ct);
        var request = await BuildRequestAsync(ct);
        if (request is null)
        {
            return Page();
        }
        try
        {
            Result = await importer.CommitAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Import commit failed");
            Error = Describe(ex);
        }
        return Page();
    }

    private async Task LoadLocationsAsync(CancellationToken ct) =>
        Locations = await db.Locations.AsNoTracking().Where(l => l.Active).OrderBy(l => l.Name).ToListAsync(ct);

    private async Task<ImportRequest?> BuildRequestAsync(CancellationToken ct)
    {
        var location = Locations.FirstOrDefault(l => l.Id == LocationId);
        if (location is null)
        {
            Error = "Escolha um local.";
            return null;
        }

        var calendarId = string.IsNullOrWhiteSpace(CalendarId) ? location.GoogleCalendarId : CalendarId.Trim();
        if (string.IsNullOrWhiteSpace(calendarId))
        {
            Error = "Indique o ID do Google Calendar (ou configure-o no local).";
            return null;
        }
        CalendarId = calendarId;

        if (To < From)
        {
            Error = "A data final tem de ser igual ou posterior à inicial.";
            return null;
        }

        var staff = await db.Staff.AsNoTracking().Where(s => s.Active).OrderBy(s => s.Name).FirstOrDefaultAsync(ct);
        if (staff is null)
        {
            Error = "Não há nenhuma profissional configurada (Clinic__StaffName).";
            return null;
        }

        return new ImportRequest(calendarId, location.Id, staff.Id, From, To);
    }

    private static string Describe(Exception ex) => ex switch
    {
        ImportConflictException c => c.Message,
        Google.GoogleApiException { HttpStatusCode: System.Net.HttpStatusCode.NotFound } =>
            "Calendário não encontrado. Confirme o ID e que o calendário está partilhado com a conta de serviço.",
        Google.GoogleApiException { HttpStatusCode: System.Net.HttpStatusCode.Forbidden } =>
            "Sem acesso ao calendário. Partilhe-o com o email da conta de serviço.",
        InvalidOperationException or ArgumentException => ex.Message,
        _ => "Ocorreu um erro inesperado. Veja os registos da aplicação.",
    };
}
