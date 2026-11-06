using System.Globalization;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Options;

namespace Osteo.Agenda.Core.Import;

public sealed class GoogleCalendarOptions
{
    /// <summary>
    /// Full JSON key of a Google service account. Each clinic calendar must be shared with the
    /// service account's e-mail ("Ver todos os detalhes do evento" is enough for importing).
    /// </summary>
    public string? ServiceAccountJson { get; set; }
}

public sealed class GoogleCalendarSource(IOptions<GoogleCalendarOptions> options) : ICalendarSource
{
    private readonly Lazy<CalendarService?> _service = new(() => Create(options.Value));

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ServiceAccountJson);

    public async Task<IReadOnlyList<CalendarEvent>> ListEventsAsync(
        string calendarId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var service = _service.Value
            ?? throw new InvalidOperationException("Google Calendar não está configurado (GoogleCalendar__ServiceAccountJson).");

        var result = new List<CalendarEvent>();
        string? pageToken = null;
        do
        {
            var request = service.Events.List(calendarId);
            request.TimeMinDateTimeOffset = from;
            request.TimeMaxDateTimeOffset = to;
            request.SingleEvents = true;
            request.ShowDeleted = false;
            request.MaxResults = 2500;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
            request.PageToken = pageToken;

            var page = await request.ExecuteAsync(ct);
            foreach (var e in page.Items ?? [])
            {
                if (e.Status == "cancelled" || e.Start is null || e.End is null)
                {
                    continue;
                }

                var allDay = e.Start.DateTimeDateTimeOffset is null;
                var start = allDay ? LocalMidnight(e.Start.Date) : e.Start.DateTimeDateTimeOffset!.Value;
                var end = allDay ? LocalMidnight(e.End.Date) : e.End.DateTimeDateTimeOffset!.Value;
                result.Add(new CalendarEvent(e.Id, e.Summary, start, end, allDay, e.ColorId));
            }
            pageToken = page.NextPageToken;
        }
        while (pageToken is not null);

        return result;
    }

    private static DateTimeOffset LocalMidnight(string? date)
    {
        var day = DateOnly.ParseExact(date ?? throw new InvalidOperationException("Evento sem data"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new DateTimeOffset(ClinicTime.ToUtc(day, TimeOnly.MinValue), TimeSpan.Zero);
    }

    private static CalendarService? Create(GoogleCalendarOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ServiceAccountJson))
        {
            return null;
        }

        var credential = CredentialFactory
            .FromJson<ServiceAccountCredential>(options.ServiceAccountJson)
            .ToGoogleCredential()
            .CreateScoped(CalendarService.Scope.CalendarReadonly);

        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Osteo Agenda",
        });
    }
}
