using System.Security.Claims;
using Kaon.Models;
using Microsoft.AspNetCore.Authentication;

namespace Kaon.Helpers;

// Signs a person in with the Kaon cookie. Used by the shared login and the separate admin login.
public static class SignIn
{
    public const string Scheme = "KaonCookie";

    public static async Task UserAsync(HttpContext http, AppUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };
        if (user.MustChangePassword) claims.Add(new Claim("must_change", "1"));
        var identity = new ClaimsIdentity(claims, Scheme);
        await http.SignInAsync(Scheme, new ClaimsPrincipal(identity),
            // Not persistent: the sign-in ends when the browser is closed
            new AuthenticationProperties { IsPersistent = false });
    }
}
