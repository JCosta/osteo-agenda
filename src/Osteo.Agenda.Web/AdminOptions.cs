namespace Osteo.Agenda.Web;

/// <summary>Single backoffice login for phase 1 (1–2 professionals). Set via Admin__Email / Admin__PasswordHash.</summary>
public sealed class AdminOptions
{
    public string? Email { get; set; }
    public string? PasswordHash { get; set; }
    public string DisplayName { get; set; } = "Administração";
}
