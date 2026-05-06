using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using DigitalTechClientPortal.Security;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class LoginController : Controller
{
    // Ajusta este valor al "landing" real de tu app tras login
    private const string DefaultPostLoginPath = "/Home";
    private readonly IConfiguration _configuration;

    public LoginController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

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
    public IActionResult SignIn([FromQuery] string? returnUrl = null, [FromQuery] bool consent = false)
    {
        var redirect = NormalizeReturnUrl(returnUrl);
        var properties = new AuthenticationProperties { RedirectUri = redirect };

        if (consent)
        {
            properties.SetParameter("prompt", "consent");
            properties.SetParameter("scope", string.Join(" ", GraphPermissionRequirements.LoginScopes));
        }

        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpGet("consent")]
    [Authorize]
    public IActionResult Consent([FromQuery] string? returnUrl = null, [FromQuery] bool admin = false)
    {
        var redirect = NormalizeReturnUrl(returnUrl);

        if (admin)
            return Redirect(BuildAdminConsentUrl(redirect));

        return RedirectToAction(nameof(SignIn), new { returnUrl = redirect, consent = true });
    }

    [HttpGet("admin-consent-result")]
    [AllowAnonymous]
    public IActionResult AdminConsentResult(
        [FromQuery(Name = "admin_consent")] string? adminConsent,
        [FromQuery] string? tenant,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        [FromQuery] string? state)
    {
        var redirect = NormalizeReturnUrl(state);

        if (string.Equals(adminConsent, "True", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(error))
        {
            return RedirectToAction(nameof(SignIn), new { returnUrl = redirect, consent = true });
        }

        var reason = string.IsNullOrWhiteSpace(errorDescription)
            ? error ?? "No se concedió el consentimiento de administrador."
            : errorDescription;

        return RedirectToAction("Error", "Home", new { reason });
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

    private string BuildAdminConsentUrl(string returnUrl)
    {
        var tenantId =
            User.FindFirst("tid")?.Value
            ?? "organizations";

        if (string.Equals(tenantId, "common", StringComparison.OrdinalIgnoreCase))
            tenantId = "organizations";

        var clientId = _configuration["AzureAd:ClientId"]
            ?? throw new InvalidOperationException("Configura AzureAd:ClientId.");

        var redirectUri = Url.Action(
            nameof(AdminConsentResult),
            "Login",
            values: null,
            protocol: Request.Scheme)
            ?? throw new InvalidOperationException("No fue posible construir redirect_uri para consentimiento.");

        var scope = string.Join(" ", GraphPermissionRequirements.TenantReadScopes
            .Select(GraphPermissionRequirements.ToGraphScope));

        var query = new QueryString()
            .Add("client_id", clientId)
            .Add("scope", scope)
            .Add("redirect_uri", redirectUri)
            .Add("state", returnUrl);

        return $"https://login.microsoftonline.com/{Uri.EscapeDataString(tenantId)}/v2.0/adminconsent{query}";
    }
}
