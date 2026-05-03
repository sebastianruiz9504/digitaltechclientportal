using DigitalTechClientPortal.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using DigitalTechClientPortal.Infrastructure.Dataverse;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using DigitalTechClientPortal.Services;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.AspNetCore.Routing;
using DigitalTechClientPortal.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Client; // app-only Graph
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims; // <-- importante: este es el ClaimTypes correcto

var builder = WebApplication.CreateBuilder(args);

// Configuración solo desde appsettings.json para Dataverse (sin variables de entorno ni secretos locales)
var appsettingsOnlyConfiguration = new ConfigurationBuilder()
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

// Servicios propios
builder.Services.AddScoped<CapacitacionService>();
builder.Services.AddScoped<GraphCalendarService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICapacitacionService, CapacitacionService>();
builder.Services.AddSingleton<GraphCalendarService>();
builder.Services.AddControllersWithViews();

// Dataverse Options
builder.Services
    .AddOptions<DataverseOptions>()
    .Bind(appsettingsOnlyConfiguration.GetSection("Dataverse"))
    .ValidateDataAnnotations()
    .Validate(o => Uri.TryCreate(o.Url, UriKind.Absolute, out _), "Dataverse:Url inválida")
    .ValidateOnStart();

builder.Services.AddScoped<ClientesService>();
builder.Services.AddHttpClient<YouTubeService>();
builder.Services.AddScoped<ReportesCloudService>();

builder.Services
    .AddOptions<DigitalTechClientPortal.Services.YouTubeOptions>()
    .Bind(builder.Configuration.GetSection("YouTube"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "YouTube:ApiKey requerido")
    .Validate(o => !string.IsNullOrWhiteSpace(o.PlaylistId), "YouTube:PlaylistId requerido")
    .ValidateOnStart();

builder.Services.AddHttpClient<DigitalTechClientPortal.Services.YouTubeService>();

// Forwarded Headers (proxy/LB)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
});

builder.Services.AddScoped<DigitalTechApp.Services.ChatService>();
builder.Services.AddScoped<DigitalTechApp.Services.SearchService>();
builder.Services.AddScoped<DigitalTechApp.Services.OpenAIClientAdapter>();

// GraphClientFactory (delegated)
builder.Services.AddScoped<GraphClientFactory>();

builder.Services.AddScoped<SecurityDataService>();

// Cookies cross-site
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.None;
    options.Secure = CookieSecurePolicy.Always;
});

// IHttpClientFactory
builder.Services.AddHttpClient();

builder.Services.AddScoped<DataverseHomeService>();

// Registrar conexión Dataverse
builder.Services.AddScoped<DigitalTechClientPortal.Services.DataverseSoporteService>();
builder.Services.AddScoped<DataverseClienteService>();
builder.Services.AddScoped<SummaryService>();

// === NUEVO: LimitedAccess ===
builder.Services.AddScoped<LimitedAccessService>(); // usa ServiceClient internamente
builder.Services.AddScoped<IClaimsTransformation, LimitedAccessClaimsTransformer>();

// Autorización global
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

// Autenticación: Cookie + OIDC
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = ".DigitalTech.Auth";
        options.Cookie.SameSite = SameSiteMode.None;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

        options.LoginPath = "/Login/Index";
        options.LogoutPath = "/Login/signout";
        options.AccessDeniedPath = "/Login/Index";

        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    })
    .AddOpenIdConnect(options =>
    {
        // Azure AD (Entra ID)
        var azureAdTenantId = builder.Configuration["AzureAd:TenantId"] ?? "common";
        options.Authority = $"https://login.microsoftonline.com/{azureAdTenantId}/v2.0";
        options.ClientId = builder.Configuration["AzureAd:ClientId"]
            ?? throw new InvalidOperationException("Configura AzureAd:ClientId.");
        options.ClientSecret = builder.Configuration["AzureAd:ClientSecret"]
            ?? throw new InvalidOperationException("Configura AzureAd:ClientSecret.");

        options.TokenValidationParameters.ValidateIssuer = false;
        options.ResponseType = "code";
        options.UsePkce = true;
        options.SaveTokens = true;
        options.CallbackPath = "/signin-oidc";

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.Scope.Add("User.Read");
        options.Scope.Add("User.Read.All");
        options.Scope.Add("Directory.Read.All");
        options.Scope.Add("SecurityEvents.Read.All");
        options.Scope.Add("offline_access");

        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "roles";

        options.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = ctx =>
            {
                
                var req = ctx.Request;
                var scheme = req.Headers["X-Forwarded-Proto"].ToString();
                var host = req.Headers["X-Forwarded-Host"].ToString();
                if (!string.IsNullOrEmpty(scheme) && !string.IsNullOrEmpty(host))
                {
                    ctx.ProtocolMessage.RedirectUri = $"{scheme}://{host}{req.PathBase}{ctx.Options.CallbackPath}";
                }
                return Task.CompletedTask;
            },
            OnRemoteFailure = ctx =>
            {
                ctx.Response.Redirect("/Home/Error?reason=" + Uri.EscapeDataString(ctx.Failure?.Message ?? "unknown"));
                ctx.HandleResponse();
                return Task.CompletedTask;
            }
        };
    });

// MVC
builder.Services.AddControllersWithViews();

// Dataverse: registro único
builder.Services.AddSingleton<IOrganizationService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<ServiceClient>>();
    var connString = appsettingsOnlyConfiguration.GetConnectionString("Dataverse");

    if (string.IsNullOrWhiteSpace(connString))
    {
        var opt = sp.GetRequiredService<IOptions<DataverseOptions>>().Value;

        if (string.IsNullOrWhiteSpace(opt.Url) ||
            string.IsNullOrWhiteSpace(opt.TenantId) ||
            string.IsNullOrWhiteSpace(opt.ClientId) ||
            string.IsNullOrWhiteSpace(opt.ClientSecret))
        {
            throw new InvalidOperationException("Configura ConnectionStrings:Dataverse o los campos de DataverseOptions (Url, TenantId, ClientId, ClientSecret).");
        }

        connString =
            $"AuthType=ClientSecret;Url={opt.Url};TenantId={opt.TenantId};ClientId={opt.ClientId};ClientSecret={opt.ClientSecret}";
    }

    var parsedPairs = connString
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
        .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
        .ToDictionary(
            parts => parts[0].Trim(),
            parts => parts[1].Trim().Trim('"', '\''),
            StringComparer.OrdinalIgnoreCase);

    var requiredKeys = new[] { "AuthType", "Url", "ClientId", "ClientSecret", "TenantId" };
    var missingKeys = requiredKeys
        .Where(key => !parsedPairs.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        .ToArray();

    if (missingKeys.Length > 0)
    {
        throw new InvalidOperationException(
            $"La cadena de conexión de Dataverse es inválida. Faltan claves requeridas: {string.Join(", ", missingKeys)}.");
    }

    if (!string.Equals(parsedPairs["AuthType"], "ClientSecret", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"La cadena de conexión de Dataverse tiene AuthType={parsedPairs["AuthType"]}. Para este portal se espera AuthType=ClientSecret.");
    }

    var rawUrl = parsedPairs["Url"];
    var urlCandidate = rawUrl;
    if (!urlCandidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
        !urlCandidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
        urlCandidate = $"https://{urlCandidate.TrimStart('/')}";
    }

    var urlForConnectionString = rawUrl;
    if (Uri.TryCreate(urlCandidate, UriKind.Absolute, out var normalizedUri) && !string.IsNullOrWhiteSpace(normalizedUri.Host))
    {
        // Cuando se puede, enviamos la URL canónica para evitar variaciones de formato.
        urlForConnectionString = normalizedUri.GetLeftPart(UriPartial.Authority);
    }
    else
    {
        // No bloquear el arranque por validación estricta de URL; delegar la validación final al SDK
        // y reportar el error real con serviceClient.LastError/ExceptionMessage.
        logger.LogWarning("Dataverse Url con formato no canónico: {DataverseUrl}. Se intentará conexión con el valor original.", rawUrl);
    }

    var normalizedConnString =
        $"AuthType=ClientSecret;Url={urlForConnectionString};TenantId={parsedPairs["TenantId"]};ClientId={parsedPairs["ClientId"]};ClientSecret={parsedPairs["ClientSecret"]}";

    ServiceClient serviceClient;
    try
    {
        // Nota: en algunas versiones del SDK, construir ServiceClient sin logger puede lanzar
        // NullReferenceException dentro de ConnectToService y ocultar el error real.
        serviceClient = new ServiceClient(normalizedConnString, logger);
    }
    catch (NullReferenceException ex)
    {
        throw new InvalidOperationException(
            "No se pudo inicializar ServiceClient. Revisa ConnectionStrings:Dataverse y valida AuthType=ClientSecret, Url, ClientId, ClientSecret y TenantId.",
            ex);
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException(
            "No se pudo crear ServiceClient para Dataverse. Revisa que Url, TenantId, ClientId y ClientSecret correspondan al mismo App Registration.",
            ex);
    }

    if (!serviceClient.IsReady)
    {
        throw new InvalidOperationException(
            $"No se pudo conectar a Dataverse. {serviceClient.LastError ?? "Error desconocido."}");
    }

    return serviceClient;
});

// También registrar ServiceClient
builder.Services.AddSingleton<ServiceClient>(sp =>
{
    return (ServiceClient)sp.GetRequiredService<IOrganizationService>();
});

builder.Services.AddScoped<CapacitacionService>();
builder.Services.AddScoped<IDataverseService, DataverseService>();
builder.Services.AddScoped<ContactsPanelService>();

// Graph app-only para Seguridad
builder.Services.AddScoped<GraphAppOnlyClientFactory>(sp =>
{
    var cfg = builder.Configuration;
    var tenantId = cfg["Graph:TenantId"];
    var clientId = cfg["Graph:ClientId"];
    var clientSecret = cfg["Graph:ClientSecret"];

    if (string.IsNullOrWhiteSpace(tenantId) ||
        string.IsNullOrWhiteSpace(clientId) ||
        string.IsNullOrWhiteSpace(clientSecret))
    {
        throw new InvalidOperationException("Configura Graph:TenantId, Graph:ClientId y Graph:ClientSecret para el cliente app-only.");
    }

    var cca = ConfidentialClientApplicationBuilder
        .Create(clientId)
        .WithClientSecret(clientSecret)
        .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
        .Build();

    return new GraphAppOnlyClientFactory(cca);
});

var app = builder.Build();

// Orden del pipeline
app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCookiePolicy();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers(); // atributos [Route]
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Login}/{action=Index}/{id?}");

// Test Dataverse (no bloquear el arranque si la conexión falla)
try
{
    var serviceClient = app.Services.GetRequiredService<ServiceClient>();
    var query = new QueryExpression("cr07a_capacitacion")
    {
        ColumnSet = new ColumnSet("cr07a_fecha")
    };

    var results = serviceClient.RetrieveMultiple(query);
    Console.WriteLine($"✅ Acceso confirmado: {results.Entities.Count} registros en cr07a_capacitacion.");
}
catch (Exception ex)
{
    Console.WriteLine($"⚠️ Dataverse no disponible durante startup: {ex}");
}

// Dump de endpoints
var dataSource = app.Services.GetRequiredService<EndpointDataSource>();
Console.WriteLine("---- ENDPOINTS CONFIGURADOS ----");
foreach (var ep in dataSource.Endpoints)
{
    Console.WriteLine(ep.DisplayName);
}
Console.WriteLine("---- FIN DE ENDPOINTS ----");

app.Run();
