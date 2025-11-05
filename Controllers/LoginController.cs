using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class LoginController : Controller
{
    // Ajusta este valor al "landing" real de tu app tras login
    private const string DefaultPostLoginPath = "/Home";

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Index([FromQuery] string? returnUrl = null)
    {
        var target = NormalizeReturnUrl(returnUrl);

        if (User.Identity?.IsAuthenticated == true)
            return LocalRedirect(target);

        ViewData["Title"] = "Iniciar sesión";
        ViewData["ReturnUrl"] = target; // para preservarlo en la vista si lo necesitas
        return View();
    }

    [HttpGet("signin")]
    [AllowAnonymous]
    public IActionResult SignIn([FromQuery] string? returnUrl = null)
    {
        var redirect = NormalizeReturnUrl(returnUrl);

        return Challenge(
            new AuthenticationProperties { RedirectUri = redirect },
            OpenIdConnectDefaults.AuthenticationScheme
        );
    }

    [HttpPost("signout")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public IActionResult SignOutUser()
    {
        return SignOut(
            new AuthenticationProperties
            {
                RedirectUri = Url.Action("SignedOut", "Login")
            },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme
        );
    }

    [HttpGet("signedout")]
    [AllowAnonymous]
    public IActionResult SignedOut()
    {
        // Tras cerrar sesión, vuelve explícitamente a la página de login
        return RedirectToAction("Index", "Login");
    }

    // Normaliza y sanea el returnUrl para evitar bucles y open redirects
    private string NormalizeReturnUrl(string? returnUrl)
    {
        // 1) Default si viene vacío o no es local
        if (string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl))
            return DefaultPostLoginPath;

        var lower = returnUrl.ToLowerInvariant();

        // 2) Evita redirigir a rutas de login o a la raíz si tu "/" cae en Login
        if (lower == "/" ||
            lower.StartsWith("/login") ||
            lower.StartsWith("/signin"))
            return DefaultPostLoginPath;

        return returnUrl;
    }
}