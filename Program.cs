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
using Microsoft.Identity.Client; // agregado para app-only Graph

var builder = WebApplication.CreateBuilder(args);

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
    .Bind(builder.Configuration.GetSection("Dataverse"))
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

// GraphClientFactory (delegated): usa el token del usuario desde HttpContext
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

// Autorización global
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

// Autenticación: Cookie + OIDC (Authorization Code + PKCE) — manteniendo lo tuyo
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
        options.Authority = "https://login.microsoftonline.com/common/v2.0";
        options.ClientId = "28c4c8cf-5a82-4744-8d16-6cb007a645d5";
        options.ClientSecret = "-ox8Q~SLdEt1JJnuCL0qdoR~w5XmR33jAghgIbaZ";

        options.TokenValidationParameters.ValidateIssuer = false;
        options.ResponseType = "code";
        options.UsePkce = true;
        options.SaveTokens = true;
        options.CallbackPath = "/signin-oidc";

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        // Scopes mínimos para Graph en delegado (ya ajustados según lo que estás leyendo)
        options.Scope.Add("User.Read");
        options.Scope.Add("User.Read.All");
        options.Scope.Add("SecurityEvents.Read.All");
        options.Scope.Add("offline_access"); // para refresh tokens

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
    var connString = builder.Configuration.GetConnectionString("Dataverse");

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

    return new ServiceClient(connString);
});

// También registrar ServiceClient para clases que lo pidan directamente
builder.Services.AddSingleton<ServiceClient>(sp =>
{
    return (ServiceClient)sp.GetRequiredService<IOrganizationService>();
});

builder.Services.AddScoped<CapacitacionService>();
builder.Services.AddScoped<IDataverseService, DataverseService>();
builder.Services.AddScoped<ContactsPanelService>();

// ---------------------------
// Graph app-only para Seguridad
// ---------------------------
// Usa configuración del app registration (sección "Graph" en appsettings.json):
// Graph:TenantId, Graph:ClientId, Graph:ClientSecret
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

// Test Dataverse
var serviceClient = app.Services.GetRequiredService<ServiceClient>();
try
{
    var query = new QueryExpression("cr07a_capacitacion")
    {
        ColumnSet = new ColumnSet("cr07a_fecha")
    };

    var results = serviceClient.RetrieveMultiple(query);
    Console.WriteLine($"✅ Acceso confirmado: {results.Entities.Count} registros en cr07a_capacitacion.");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error al acceder a cr07a_capacitacion: {ex.Message}");
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