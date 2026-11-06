using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Osteo.Agenda.Core.Security;

namespace Osteo.Agenda.Web.Pages;

[EnableRateLimiting("login")]
public sealed class LoginModel(IOptions<AdminOptions> admin) : PageModel
{
    [BindProperty, Required, EmailAddress]
    public string Email { get; set; } = "";

    [BindProperty, Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool NotConfigured => string.IsNullOrEmpty(admin.Value.Email) || string.IsNullOrEmpty(admin.Value.PasswordHash);

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var o = admin.Value;
        var emailOk = string.Equals(Email.Trim(), o.Email, StringComparison.OrdinalIgnoreCase);
        var passwordOk = PasswordHasher.Verify(Password, o.PasswordHash);
        if (!emailOk || !passwordOk)
        {
            ModelState.AddModelError(string.Empty, "Email ou palavra-passe incorretos.");
            return Page();
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, o.DisplayName), new Claim(ClaimTypes.Email, o.Email!)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
    }
}
