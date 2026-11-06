using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Osteo.Agenda.Core.Contacts;
using Osteo.Agenda.Core.Data;
using Osteo.Agenda.Core.Import;
using Osteo.Agenda.Core.Security;
using Osteo.Agenda.Core.Setup;
using Osteo.Agenda.Web;

// Utility: `dotnet run -- hash-password "a minha password"` prints a value for Admin__PasswordHash.
if (args is ["hash-password", var plain])
{
    Console.WriteLine(PasswordHasher.Hash(plain));
    return;
}

var builder = WebApplication.CreateBuilder(args);

var rawConnection = builder.Configuration["DATABASE_URL"]
    ?? builder.Configuration.GetConnectionString("Agenda")
    ?? throw new InvalidOperationException("Defina DATABASE_URL ou ConnectionStrings:Agenda.");
var connectionString = ConnectionStrings.Normalize(rawConnection);

builder.Services.AddDbContext<AgendaDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.Configure<ClinicOptions>(builder.Configuration.GetSection("Clinic"));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection("Admin"));
builder.Services.Configure<GoogleCalendarOptions>(builder.Configuration.GetSection("GoogleCalendar"));

builder.Services.AddSingleton<ICalendarSource, GoogleCalendarSource>();
builder.Services.AddScoped<CalendarImportService>();
builder.Services.AddScoped<ContactQueries>();
builder.Services.AddScoped<ClinicSeeder>();

builder.Services.AddDataProtection()
    .SetApplicationName("osteo-agenda")
    .PersistKeysToDbContext<AgendaDbContext>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Login";
        o.LogoutPath = "/Logout";
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
        o.SlidingExpiration = true;
        o.Cookie.Name = "osteo.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5) }));
});

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/");
    o.Conventions.AllowAnonymousToPage("/Login");
    o.Conventions.AllowAnonymousToPage("/Error");
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // Render (and most PaaS) terminate TLS at their proxy.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

await SqlMigrator.MigrateAsync(connectionString);
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<ClinicSeeder>().SeedAsync();
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", async (AgendaDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok("ok") : Results.StatusCode(503));
app.MapRazorPages();

app.Run();

public partial class Program;
