using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Vita.App;
using Vita.App.Components;
using Vita.App.Data;
using Vita.App.Services;

// Keep the executable feeling like a desktop app: a second double-click only opens the app again.
using var instanceMutex = new Mutex(initiallyOwned: true, @"Local\Vita.App.SingleInstance", out var isFirstInstance);
if (!isFirstInstance)
{
    OpenWhenReady();
    return;
}

// The published build runs without a console, so fatal startup errors go to a log and a dialog.
var diagnosticsDir = Path.Combine(AppContext.BaseDirectory, "App_Data");
try { Directory.CreateDirectory(diagnosticsDir); } catch { /* read-only folder */ }

AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    var error = eventArgs.ExceptionObject?.ToString() ?? "Erro desconhecido.";
    var logPath = Path.Combine(diagnosticsDir, "vita-error.log");
    try { File.AppendAllText(logPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {error}{Environment.NewLine}{Environment.NewLine}"); }
    catch { /* nothing else we can do */ }

    var summary = eventArgs.ExceptionObject is Exception ex
        ? $"{ex.GetType().Name}: {ex.Message}"
        : "Erro desconhecido.";
    if (summary.Length > 500) summary = summary[..500] + "…";

    NativeDialog.ShowError("Vita",
        "A Vita encontrou um problema e precisou ser encerrada.\n\n" +
        summary + "\n\n" +
        "Se o problema continuar, feche outras cópias da Vita e tente novamente.\n" +
        "Detalhes técnicos: " + logPath);
};

var builder = WebApplication.CreateBuilder(args);

// If the preferred port is taken (another local app, a second Windows user, etc.),
// move to the next free port instead of failing to start.
var urls = ResolveStartupUrls(builder);
builder.WebHost.UseUrls(urls);

var configuredDataDir = builder.Configuration["Vita:DataDir"];
var dataDir = string.IsNullOrWhiteSpace(configuredDataDir)
    ? Path.Combine(builder.Environment.ContentRootPath, "App_Data")
    : Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredDataDir));
Directory.CreateDirectory(dataDir);
var keysDir = Path.Combine(dataDir, "keys");
Directory.CreateDirectory(keysDir);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(2));

builder.Services.AddAuthentication("VitaCookie")
    .AddCookie("VitaCookie", options =>
    {
        options.Cookie.Name = "vita.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/auth"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddDataProtection()
    .SetApplicationName("Vita");

var profileManager = new ProfileManager(dataDir);
builder.Services.AddSingleton(profileManager);
builder.Services.AddScoped<IDbContextFactory<AppDbContext>, ProfileDbContextFactory>();

builder.Services.AddHttpClient("ai");
builder.Services.AddHttpClient("jobs", client =>
{
    client.Timeout = TimeSpan.FromSeconds(25);
    client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Vita/1.0 (local job search)");
});

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UserSession>();
builder.Services.AddScoped<AiSettingsService>();
builder.Services.AddSingleton<AiClient>();
builder.Services.AddSingleton<OllamaSetupService>();
builder.Services.AddScoped<AiAssistantService>();
builder.Services.AddScoped<ResumeStore>();
builder.Services.AddScoped<ApplicationStore>();
builder.Services.AddScoped<CoverLetterStore>();
builder.Services.AddScoped<ChatStore>();
builder.Services.AddScoped<JobBoardService>();
builder.Services.AddSingleton<PdfService>();
builder.Services.AddSingleton<DocxService>();
builder.Services.AddSingleton<ImportService>();

builder.Services.AddSingleton<ToastService>();

var app = builder.Build();

await LegacyProfileMigrator.RunAsync(profileManager, dataDir, app.Logger);

app.MapStaticAssets();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Vita-App"] = "1";
    await next(context);
});
app.UseAuthentication();
app.Use(async (context, next) =>
{
    // Resolve the profile from the auth cookie before any scoped service opens a database.
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var slug = context.User.FindFirst("vita_profile")?.Value;
        var profiles = context.RequestServices.GetRequiredService<ProfileManager>();
        var profile = profiles.Resolve(slug);
        if (profile is not null) profiles.SetCurrent(profile);
    }
    await next();
});
app.UseAuthorization();
app.UseAntiforgery();

// ------------------------------------------------------------------ auth endpoints
app.MapPost("/auth/login", async (HttpContext http, AuthService auth) =>
{
    var form = await http.Request.ReadFormAsync();
    var result = await auth.LoginAsync(form["name"].ToString());
    if (result.User is null || result.Profile is null)
        return Results.Json(new { error = result.Error ?? "Não foi possível abrir o perfil." }, statusCode: StatusCodes.Status400BadRequest);

    await http.SignInAsync("VitaCookie", AuthService.BuildPrincipal(result.User, result.Profile), new AuthenticationProperties
    {
        IsPersistent = true,
        ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
    });
    return Results.Ok(new { ok = true, name = result.User.FullName });
}).DisableAntiforgery();

app.MapPost("/auth/logout", async (HttpContext http, ProfileManager profiles) =>
{
    profiles.SetCurrent(null);
    await http.SignOutAsync("VitaCookie");
    return Results.Ok(new { ok = true });
}).DisableAntiforgery();

app.MapPost("/perfil/abrir-pasta", (HttpContext http, ProfileManager profiles) =>
{
    if (http.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    if (profiles.Current is not { } profile) return Results.BadRequest(new { error = "Nenhum perfil ativo." });
    try
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{profile.Directory}\"") { UseShellExecute = true });
        return Results.Ok(new { ok = true, path = profile.Directory });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
}).DisableAntiforgery();

// ------------------------------------------------------------------ file downloads
app.MapGet("/api/resumes/{id:guid}/pdf", async (Guid id, HttpContext http, ResumeStore store, PdfService pdf) =>
{
    if (GetUserId(http.User) is not { } userId) return Results.Unauthorized();
    var entity = await store.GetAsync(id, userId);
    if (entity is null) return Results.NotFound();
    var document = Json.Deserialize<ResumeDocument>(entity.DataJson) ?? new ResumeDocument();
    var bytes = pdf.RenderResume(document, entity.TemplateId, entity.AccentColor);
    return Results.File(bytes, "application/pdf", FileName(document.Contact.FullName, "curriculo") + ".pdf");
});

app.MapGet("/api/resumes/{id:guid}/docx", async (Guid id, HttpContext http, ResumeStore store, DocxService docx) =>
{
    if (GetUserId(http.User) is not { } userId) return Results.Unauthorized();
    var entity = await store.GetAsync(id, userId);
    if (entity is null) return Results.NotFound();
    var document = Json.Deserialize<ResumeDocument>(entity.DataJson) ?? new ResumeDocument();
    var bytes = docx.RenderResume(document, entity.AccentColor);
    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        FileName(document.Contact.FullName, "curriculo") + ".docx");
});

app.MapGet("/api/cover-letters/{id:guid}/pdf", async (Guid id, HttpContext http, CoverLetterStore letters, ResumeStore resumes, PdfService pdf) =>
{
    if (GetUserId(http.User) is not { } userId) return Results.Unauthorized();
    var letter = await letters.GetAsync(id, userId);
    if (letter is null) return Results.NotFound();
    var resume = letter.ResumeId is { } resumeId ? await resumes.GetDocumentAsync(resumeId, userId) : null;
    resume ??= new ResumeDocument();
    var bytes = pdf.RenderCoverLetter(resume, letter.Body, "#1F6F5C");
    return Results.File(bytes, "application/pdf", FileName(letter.Company, "carta") + "-carta.pdf");
});

app.MapGet("/api/export", async (HttpContext http, ResumeStore resumes, ApplicationStore applications,
    CoverLetterStore letters, JobBoardService jobs) =>
{
    if (GetUserId(http.User) is not { } userId) return Results.Unauthorized();
    var payload = new
    {
        exportedAt = DateTime.UtcNow,
        resumes = await resumes.ListAsync(userId),
        applications = await applications.ListAsync(userId),
        coverLetters = await letters.ListAsync(userId),
        savedJobs = await jobs.ListSavedAsync(userId)
    };
    var json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    return Results.File(Encoding.UTF8.GetBytes(json), "application/json", "vita-dados.json");
}).DisableAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// ------------------------------------------------------------------ helpers
static Guid? GetUserId(ClaimsPrincipal principal) =>
    Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

static string FileName(string? value, string fallback)
{
    var name = TextAnalysis.Normalize(value ?? string.Empty);
    name = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-z0-9]+", "-").Trim('-');
    return string.IsNullOrWhiteSpace(name) ? fallback : name;
}

// Open the app in the default browser on first start (disabled with VITA_NO_BROWSER=1).
if (builder.Configuration.GetValue("Vita:OpenBrowser", true))
{
    var url = urls.Split(';').FirstOrDefault() ?? "http://localhost:5180";
    app.Lifetime.ApplicationStarted.Register(() => LaunchBrowser(url));
}

app.Run();

// ------------------------------------------------------------------ startup helpers
static string ResolveStartupUrls(WebApplicationBuilder builder)
{
    var configured = builder.Configuration["Urls"];
    if (string.IsNullOrWhiteSpace(configured)) configured = "http://localhost:5180";

    var parts = configured.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length != 1 ||
        !Uri.TryCreate(parts[0], UriKind.Absolute, out var uri) ||
        !uri.IsLoopback ||
        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
    {
        // Explicit address, LAN binding or several URLs: respect the configuration as-is.
        return configured;
    }

    for (var port = uri.Port; port < uri.Port + 20; port++)
    {
        if (!IsPortFree(port)) continue;
        return port == uri.Port ? configured : $"{uri.Scheme}://{uri.Host}:{port}";
    }

    return configured; // let Kestrel report the conflict if every candidate is taken
}

static bool IsPortFree(int port)
{
    try
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        listener.Stop();
        return true;
    }
    catch (SocketException)
    {
        return false;
    }
}

// A second launch waits for the first instance, finds the port it is using and opens the browser.
static void OpenWhenReady()
{
    const int firstPort = 5180;
    var deadline = DateTime.UtcNow.AddSeconds(25);
    using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };

    while (DateTime.UtcNow < deadline)
    {
        for (var port = firstPort; port < firstPort + 20; port++)
        {
            try
            {
                using var response = probe.GetAsync($"http://localhost:{port}/login").GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode &&
                    response.Headers.TryGetValues("X-Vita-App", out var values) &&
                    values.Any(value => value == "1"))
                {
                    LaunchBrowser($"http://localhost:{port}");
                    return;
                }
            }
            catch
            {
                // first instance may still be starting
            }
        }

        Thread.Sleep(300);
    }

    LaunchBrowser($"http://localhost:{firstPort}");
}

static void LaunchBrowser(string url)
{
    if (string.Equals(Environment.GetEnvironmentVariable("VITA_NO_BROWSER"), "1", StringComparison.Ordinal)) return;
    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    catch { /* no browser available */ }
}
