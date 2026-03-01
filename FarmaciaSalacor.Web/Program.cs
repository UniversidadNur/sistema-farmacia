using FarmaciaSalacor.Web.Data;
using FarmaciaSalacor.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using System.Net;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// En contenedores (Railway), Kestrel debe escuchar en 0.0.0.0.
// Railway expone el puerto en la variable PORT. Si no existe, usamos 8080 como default.
var portEnv = Environment.GetEnvironmentVariable("PORT");
var portToUse = 8080;
if (!string.IsNullOrWhiteSpace(portEnv) && int.TryParse(portEnv, out var parsedPort) && parsedPort > 0)
{
    portToUse = parsedPort;
}

builder.WebHost.UseUrls($"http://0.0.0.0:{portToUse}");

// Add services to the container.
var mvcBuilder = builder.Services.AddControllersWithViews();
if (builder.Environment.IsDevelopment())
{
    mvcBuilder.AddRazorRuntimeCompilation();
}

var defaultConnection = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(defaultConnection))
{
    // Permite arrancar (y pasar healthchecks) aun cuando el entorno no tenga variables.
    // En Railway se recomienda configurar ConnectionStrings__Default (por ejemplo: ${{ MySQL.MYSQL_URL }}).
    Console.Error.WriteLine("WARNING: ConnectionStrings:Default no está configurada. Usando SQLite local como fallback. Configure ConnectionStrings__Default en el entorno para producción.");
    defaultConnection = "Data Source=farmacia.local.db";
}

static bool LooksLikeMySqlUrl(string value)
    => value.StartsWith("mysql://", StringComparison.OrdinalIgnoreCase)
       || value.StartsWith("mariadb://", StringComparison.OrdinalIgnoreCase);

static bool LooksLikeMariaDbUrl(string value)
    => value.StartsWith("mariadb://", StringComparison.OrdinalIgnoreCase);

static string MySqlUrlToConnectionString(string url)
{
    // Formato típico Railway: mysql://user:pass@host:port/db
    // Nota: password y user pueden venir url-encoded.
    var uri = new Uri(url);

    var userInfo = uri.UserInfo.Split(':', 2);
    var user = userInfo.Length > 0 ? WebUtility.UrlDecode(userInfo[0]) : string.Empty;
    var pass = userInfo.Length > 1 ? WebUtility.UrlDecode(userInfo[1]) : string.Empty;

    var database = uri.AbsolutePath.Trim('/');
    if (string.IsNullOrWhiteSpace(database))
    {
        throw new InvalidOperationException("MYSQL_URL inválida: falta el nombre de la base de datos en la URL.");
    }

    var port = uri.IsDefaultPort ? 3306 : uri.Port;

    // Recomendación en servicios cloud: SSL requerido.
    return $"Server={uri.Host};Port={port};Database={database};User={user};Password={pass};SslMode=Required;";
}

var isMySql = LooksLikeMySqlUrl(defaultConnection)
    || defaultConnection.Contains("Server=", StringComparison.OrdinalIgnoreCase)
    || defaultConnection.Contains("Host=", StringComparison.OrdinalIgnoreCase);

var isMariaDb = LooksLikeMariaDbUrl(defaultConnection)
    || defaultConnection.Contains("MariaDb", StringComparison.OrdinalIgnoreCase)
    || defaultConnection.Contains("MariaDB", StringComparison.OrdinalIgnoreCase);

var connectionToUse = defaultConnection;
if (LooksLikeMySqlUrl(connectionToUse))
{
    connectionToUse = MySqlUrlToConnectionString(connectionToUse);
}

// Si la connection string de MySQL está mal formada, MySqlConnector puede lanzar excepciones
// que terminan en HTTP 500 en cualquier request. Preferimos hacer fallback a SQLite y loguear.
if (isMySql)
{
    try
    {
        _ = new MySqlConnectionStringBuilder(connectionToUse);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"WARNING: ConnectionStrings:Default parece inválida para MySQL. Se usará SQLite fallback. Error: {ex.Message}");
        isMySql = false;
        isMariaDb = false;
        connectionToUse = "Data Source=farmacia.local.db";
    }
}


builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (isMySql)
    {
        // Evita ServerVersion.AutoDetect(): intenta conectar a la DB para detectar la versión y,
        // si la red/credenciales fallan, puede tirar HTTP 500 incluso en /Account/Login.
        // Railway suele usar MySQL 8; si usas MariaDB, ajusta mediante isMariaDb.
        ServerVersion serverVersion = isMariaDb
            ? new MariaDbServerVersion(new Version(10, 6, 0))
            : new MySqlServerVersion(new Version(8, 0, 0));

        options.UseMySql(connectionToUse, serverVersion, mySqlOptions => mySqlOptions.EnableRetryOnFailure());
    }
    else
    {
        options.UseSqlite(connectionToUse);
    }
});

builder.Services.AddHostedService<DbInitializerHostedService>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Denied";
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

// Healthcheck para Railway: debe responder 200 rápido y sin redirecciones.
// Se ubica antes de UseHttpsRedirection/Auth para evitar 30x/401.
app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsync("OK");
        return;
    }

    await next();
});

// Railway y otros PaaS suelen pasar la IP y el esquema real por headers.
// Esto evita bucles con UseHttpsRedirection cuando hay terminación TLS en el proxy.
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    // Railway puede encadenar proxies; no limitamos estricto.
    ForwardLimit = null
};

// Importante: hay que limpiar los defaults (loopback). Un initializer vacío no los borra.
forwardedOptions.KnownNetworks.Clear();
forwardedOptions.KnownProxies.Clear();

app.UseForwardedHeaders(forwardedOptions);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    // Permite activar el detalle de errores en producción SOLO para debug.
    // En Railway se puede setear FARMACIA_DEBUG_ERRORS=true temporalmente y luego quitarlo.
    var debugErrors = string.Equals(Environment.GetEnvironmentVariable("FARMACIA_DEBUG_ERRORS"), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("FARMACIA_DEBUG_ERRORS"), "1", StringComparison.OrdinalIgnoreCase);

    if (debugErrors)
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        // Importante: NO usar "/Home/Error" aquí.
        // Si el problema real es AppDbContext/DI, HomeController también falla y terminamos con 500 sin diagnóstico.
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var feature = context.Features.Get<IExceptionHandlerFeature>();
                var ex = feature?.Error;

                if (ex is not null)
                {
                    app.Logger.LogError(ex, "Unhandled exception. TraceId={TraceId}", context.TraceIdentifier);
                    Console.Error.WriteLine($"Unhandled exception. TraceId={context.TraceIdentifier}\n{ex}");
                }

                if (!context.Response.HasStarted)
                {
                    context.Response.Clear();
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = "text/plain; charset=utf-8";
                }

                await context.Response.WriteAsync($"Ocurrió un error interno. TraceId: {context.TraceIdentifier}. Revise los logs del servidor.");
            });
        });
    }

    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// En Railway, HTTPS termina en el proxy. Si por algún motivo no llegan los forwarded headers,
// UseHttpsRedirection puede entrar en loop (http interno -> redirect a https externo -> http interno ...).
var isRailway = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RAILWAY_SERVICE_ID"))
    || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RAILWAY_PROJECT_ID"))
    || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RAILWAY_ENVIRONMENT"));

var isBehindProxy = isRailway || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PORT"));

if (!isBehindProxy)
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Diagnóstico rápido (sin exponer secretos). Útil para verificar despliegue/variables en Railway.
app.MapGet("/health/details", (HttpContext ctx) =>
{
    var asm = Assembly.GetExecutingAssembly().GetName();
    var version = asm.Version?.ToString() ?? "unknown";
    var env = app.Environment.EnvironmentName;

    var resetActive = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FARMACIA_RESET_ADMIN_PASSWORD"));
    var resetUsername = Environment.GetEnvironmentVariable("FARMACIA_RESET_ADMIN_USERNAME") ?? "admin";

    var railwayServiceId = Environment.GetEnvironmentVariable("RAILWAY_SERVICE_ID") ?? string.Empty;
    var railwayProjectId = Environment.GetEnvironmentVariable("RAILWAY_PROJECT_ID") ?? string.Empty;

    var text = $"OK\n" +
               $"Env: {env}\n" +
               $"Version: {version}\n" +
               $"ResetActive: {resetActive}\n" +
               $"ResetUsername: {resetUsername}\n" +
               $"RailwayServiceId: {(string.IsNullOrWhiteSpace(railwayServiceId) ? "(empty)" : railwayServiceId)}\n" +
               $"RailwayProjectId: {(string.IsNullOrWhiteSpace(railwayProjectId) ? "(empty)" : railwayProjectId)}\n" +
               $"TraceId: {ctx.TraceIdentifier}\n";

    return Results.Text(text, "text/plain; charset=utf-8");
}).AllowAnonymous();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
