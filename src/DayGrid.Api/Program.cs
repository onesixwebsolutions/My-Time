using System.Text.Json;
using System.Text.Json.Serialization;
using DayGrid.Api;
using DayGrid.Api.Auth;
using DayGrid.Api.Endpoints;
using DayGrid.Api.Hubs;
using DayGrid.Api.Security;
using DayGrid.Application.Scheduling;
using DayGrid.Application.Security;
using DayGrid.Application.Time;
using DayGrid.Infrastructure.BackgroundServices;
using DayGrid.Infrastructure.Data;
using DayGrid.Infrastructure.Email;
using DayGrid.Infrastructure.Notifications;
using DayGrid.Infrastructure.Scheduling;
using DayGrid.Infrastructure.Security;
using DayGrid.Infrastructure.Time;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using MysticMind.PostgresEmbed;
using Serilog;

// ContentRootPath defaults to the process's current working directory, not the executable's own
// folder — harmless when launched via `dotnet run`/an IDE (cwd is already the project folder),
// but wrong for the published single-file exe if it's ever started from a shortcut or script
// with a different "working directory" (Explorer double-click happens to get this right by
// coincidence, but nothing else does). Anchoring explicitly to AppContext.BaseDirectory makes
// wwwroot/appsettings.json resolve correctly regardless of the caller's cwd.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// ---------------------------------------------------------------------
// Serilog
// ---------------------------------------------------------------------
// ReadFrom.Configuration's "WriteTo": [{ "Name": "Console" }] in appsettings.json resolves that
// sink by reflecting over loaded assemblies to find the matching extension method — which finds
// nothing under PublishSingleFile=true (assemblies aren't laid out as discoverable files on
// disk) and throws at startup. Passing the sink assembly explicitly skips that scan entirely,
// so appsettings.json's Serilog:MinimumLevel/WriteTo config keeps working in both single-file
// and normal builds.
var serilogReaderOptions = new Serilog.Settings.Configuration.ConfigurationReaderOptions(
    typeof(Serilog.ConsoleLoggerConfigurationExtensions).Assembly);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration, serilogReaderOptions)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());
// (No extra .WriteTo.Console() here: appsettings.json's Serilog:WriteTo already adds the console
// sink, and adding it twice printed every log line twice.)

// ---------------------------------------------------------------------
// Data
// ---------------------------------------------------------------------
// Database:Mode = "Embedded" (the default — see appsettings.json) spins up a private Postgres
// instance under %LocalAppData%\DayGrid\pgdata so the app works out of the box on a machine
// that doesn't have Postgres installed. Local dev overrides this to "External" in
// appsettings.Development.json, so `dotnet run`/docker-compose keep using ConnectionStrings:Default
// exactly as before. See EmbeddedDatabase.cs for what "Embedded" actually does.
var databaseMode = builder.Configuration["Database:Mode"] ?? "External";
PgServer? embeddedPg = null;
string connectionString;

if (string.Equals(databaseMode, "Embedded", StringComparison.OrdinalIgnoreCase))
{
    // Azure App Service sets WEBSITE_INSTANCE_ID. An embedded Postgres there would download
    // binaries into the app's writable area and silently lose data on every restart/scale-out —
    // fail fast instead of falling back to it because Database__Mode was forgotten.
    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID")))
        throw new InvalidOperationException(
            "Database:Mode is 'Embedded' but the app is running on Azure App Service. " +
            "Set the app setting Database__Mode=External and ConnectionStrings__Default.");

    var dataDir = builder.Configuration["Database:EmbeddedDataDir"] is { Length: > 0 } configuredDir
        ? configuredDir
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DayGrid", "pgdata");
    (embeddedPg, connectionString) = await EmbeddedDatabase.StartAsync(dataDir);
}
else
{
    connectionString =
        builder.Configuration.GetConnectionString("Default")
        ?? "Host=localhost;Database=daygrid;Username=daygrid;Password=dev";
}

// The context is tenant-scoped: ICurrentUser (the signed-in user, or the user a background job
// acts for) drives its global query filters and insert stamping — see AppDbContext.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUserContext>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserContext>());
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

// Readiness probe (/health/ready) — verifies the database is reachable. /health stays a pure
// liveness check so an App Service health-check ping never depends on the database.
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

// ---------------------------------------------------------------------
// Reverse proxy — App Service terminates TLS at its front ends and forwards plain HTTP with
// X-Forwarded-For/-Proto. Those front ends have no fixed IPs, so trust the headers from any proxy.
// ---------------------------------------------------------------------
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ---------------------------------------------------------------------
// CORS — the Angular dev server, with credentials so SignalR's negotiate/cookie flow works.
// ---------------------------------------------------------------------
// Only registered when needed: in Development (default http://localhost:4200), or anywhere when
// Cors:AllowedOrigins is set explicitly. Production serves the SPA and the API from one origin and
// has no CORS policy at all unless configured.
const string AngularDevCorsPolicy = "AngularDev";
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() is { Length: > 0 } configuredOrigins
    ? configuredOrigins
    : builder.Environment.IsDevelopment() ? new[] { "http://localhost:4200" } : null;
if (corsOrigins is not null)
{
    builder.Services.AddCors(options => options.AddPolicy(AngularDevCorsPolicy, policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));
}

// ---------------------------------------------------------------------
// Request size — the API's JSON bodies are tiny; cap them (Kestrel, plus RequestBodyLimitMiddleware
// for a clean 413 problem response and for hosts other than Kestrel).
// ---------------------------------------------------------------------
var maxRequestBodyBytes = Math.Max(1024, builder.Configuration.GetValue<long>("Limits:MaxRequestBodyBytes", RequestBodyLimitMiddleware.DefaultMaxBytes));
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxRequestBodyBytes);

// ---------------------------------------------------------------------
// Authentication & authorization — ASP.NET Core Identity with the daygrid.auth cookie,
// antiforgery (XSRF-TOKEN cookie / X-XSRF-TOKEN header), a fallback policy requiring an
// authenticated, email-confirmed user on every endpoint, the Admin policy, the auth rate limiter
// and Data Protection keys in the database. See Auth/AuthSetup.cs and the auth contract.
// ---------------------------------------------------------------------
builder.Services.AddDayGridAuth(builder.Configuration);

// ---------------------------------------------------------------------
// SignalR — [Authorize] hub; events go to Clients.User(userId) only.
// ---------------------------------------------------------------------
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserNotifier, SignalRUserNotifier>();

// ---------------------------------------------------------------------
// JSON — camelCase is System.Text.Json's default policy for minimal APIs, but we set it
// explicitly here so it's obvious and doesn't silently depend on defaults changing upstream.
// ---------------------------------------------------------------------
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    // Enum *names* are kept verbatim (PascalCase, e.g. "Open", "FixedTime", "High") —
    // only property names go through the camelCase policy above. This matches the plan's
    // JSON examples (section 5) and what the Angular DTOs expect (e.g. SimpleTask.status:
    // 'Open' | 'Done'). Do not add JsonNamingPolicy.CamelCase here — it would lowercase
    // enum values too ("open" instead of "Open") and silently break every string
    // comparison on the frontend.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// ---------------------------------------------------------------------
// Swagger
// ---------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "DayGrid API",
        Version = "v1",
        Description = "Personal daily checklist & timetable API. Cookie auth: call GET /api/v1/auth/me, " +
                      "then POST /api/v1/auth/login; unsafe requests need the X-XSRF-TOKEN header (value of the XSRF-TOKEN cookie)."
    });
    // Swagger UI runs on the same origin, so the browser sends the daygrid.auth cookie itself;
    // the antiforgery header has to be supplied by hand (Authorize button).
    var xsrf = new OpenApiSecurityScheme
    {
        Name = AuthSupport.XsrfHeaderName,
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Copy the value of the XSRF-TOKEN cookie (issued by GET /api/v1/auth/me) here.",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "xsrf" }
    };
    options.AddSecurityDefinition("xsrf", xsrf);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [xsrf] = Array.Empty<string>() });
});

// ---------------------------------------------------------------------
// Email
// ---------------------------------------------------------------------
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
builder.Services.AddScoped<MailKitEmailSender>();
builder.Services.AddScoped<PickupDirectoryEmailSender>();
// Email:Mode = Smtp (default) | Pickup (.eml files in Email:PickupDirectory — dev and e2e tests).
builder.Services.AddScoped<IEmailSender>(sp =>
    sp.GetRequiredService<IOptions<EmailSettings>>().Value.Mode == EmailDeliveryMode.Pickup
        ? sp.GetRequiredService<PickupDirectoryEmailSender>()
        : sp.GetRequiredService<MailKitEmailSender>());
// Account emails (confirm, reset, already-registered, password-changed) go through a bounded
// background queue with per-recipient cooldown/daily caps (Email:AccountEmails) — see AccountEmailQueue.
builder.Services.Configure<AccountEmailOptions>(builder.Configuration.GetSection("Email:AccountEmails"));
builder.Services.AddSingleton<AccountEmailQueue>();
builder.Services.AddSingleton<IAccountEmailQueue>(sp => sp.GetRequiredService<AccountEmailQueue>());
builder.Services.AddHostedService<AccountEmailDispatcher>();

// ---------------------------------------------------------------------
// Application services
// ---------------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
// Time zones are per user: IAppClock in a request is the signed-in user's clock (their
// app_settings.time_zone, falling back to App:TimeZone). Background jobs use the factory.
builder.Services.AddSingleton<IAppClockFactory>(sp => new AppClockFactory(
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<IConfiguration>()["App:TimeZone"] ?? AppClock.DefaultTimeZoneId));
builder.Services.AddScoped<IAppClock, UserAppClock>();
builder.Services.AddScoped<IDayPlanBuilder, DayPlanBuilder>();

// ---------------------------------------------------------------------
// Background services
// ---------------------------------------------------------------------
builder.Services.AddHostedService<ReminderDispatcherService>();
builder.Services.AddHostedService<DailyDigestService>();

var app = builder.Build();

// Resolve the clock factory eagerly so a bad App:TimeZone fails at startup, not on the first request.
var defaultClock = app.Services.GetRequiredService<IAppClockFactory>().ForTimeZone(null);
app.Logger.LogInformation("Default time zone: {TimeZone}; local time now {Now:yyyy-MM-dd HH:mm zzz}", defaultClock.TimeZone.Id, defaultClock.Now);

// Email links (confirmation, password reset) must point at the public site. Fail fast in
// Production rather than mailing localhost links. The desktop exe (Embedded mode) runs on
// localhost by design and falls back to the request origin.
if (app.Environment.IsProduction()
    && embeddedPg is null
    && !Uri.TryCreate(app.Configuration["App:PublicBaseUrl"], UriKind.Absolute, out _))
{
    throw new InvalidOperationException(
        "App:PublicBaseUrl must be set to the site's absolute public URL in Production (e.g. App__PublicBaseUrl=https://daygrid.example.com).");
}

// Who becomes the first administrator (at email confirmation) — see BootstrapAdminPolicy.
var bootstrapAdmin = app.Services.GetRequiredService<BootstrapAdminPolicy>();
if (bootstrapAdmin.Mode == BootstrapAdminMode.None && app.Environment.IsProduction())
    app.Logger.LogWarning(
        "Auth:BootstrapAdminEmail is not set: no account will be granted Admin automatically. Set Auth__BootstrapAdminEmail " +
        "to the administrator's email address; that account becomes Admin (and claims any legacy data) when it confirms its email.");
else
    app.Logger.LogInformation("Bootstrap admin mode: {Mode}", bootstrapAdmin.Mode);

if (AuthSetup.KeyVaultKeyUri(app.Configuration) is null && app.Environment.IsProduction() && embeddedPg is null)
    app.Logger.LogWarning(
        "DataProtection:KeyVaultKeyUri is not set: the Data Protection key ring (which protects auth cookies and tokens) " +
        "is stored unencrypted in the database. Set DataProtection__KeyVaultKeyUri to an Azure Key Vault key to encrypt it at rest.");

// ---------------------------------------------------------------------
// Schema migrations (db/migrations/NNNN_*.sql, embedded in DayGrid.Infrastructure). External
// mode: opt-in via Database:InitializeSchema=true (Azure, docker-compose, local dev). Embedded
// mode already migrated in EmbeddedDatabase.StartAsync. A database created from the old
// db/init.sql is baselined at 0001 and upgraded. Never seeds data.
// ---------------------------------------------------------------------
if (embeddedPg is null && app.Configuration.GetValue<bool>("Database:InitializeSchema"))
{
    const int maxAttempts = 5;
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            await SchemaMigrator.MigrateAsync(connectionString, app.Logger);
            break;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            app.Logger.LogWarning(ex, "Schema migration attempt {Attempt}/{MaxAttempts} failed — retrying", attempt, maxAttempts);
            await Task.Delay(TimeSpan.FromSeconds(5 * attempt));
        }
        catch (Exception ex)
        {
            // Keep the host up so /health and logs stay reachable; /health/ready reports the DB state.
            app.Logger.LogError(ex, "Schema migration failed after {MaxAttempts} attempts — database may be missing its schema", maxAttempts);
        }
    }
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<RequestBodyLimitMiddleware>(maxRequestBodyBytes);

app.UseSerilogRequestLogging();

// ---------------------------------------------------------------------
// ProblemDetails-shaped error handling for everything unhandled.
// ---------------------------------------------------------------------
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    // Malformed request bodies/route values surface as BadHttpRequestException (thrown in
    // Development) — those are client errors, not 500s.
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var badRequest = error as BadHttpRequestException;
    // Values the database rejects (too long for a varchar, out of numeric(12,2) range, a NUL
    // character, a dangling foreign key, a violated CHECK/unique constraint) are client errors too.
    var dbStatus = DatabaseErrors.ToStatusCode(error);
    // A write referencing another user's row is reported exactly like a dangling reference.
    var tenantViolation = error as TenantViolationException;
    var tenantStatus = tenantViolation is null ? (int?)null
        : tenantViolation.IsReference ? StatusCodes.Status400BadRequest : StatusCodes.Status404NotFound;
    var status = badRequest?.StatusCode ?? dbStatus ?? tenantStatus ?? StatusCodes.Status500InternalServerError;
    var isClientError = status < 500;
    context.Response.StatusCode = status;
    context.Response.ContentType = "application/problem+json";
    var problem = new
    {
        type = !isClientError ? "https://tools.ietf.org/html/rfc7231#section-6.6.1"
            : status == StatusCodes.Status409Conflict ? "https://tools.ietf.org/html/rfc7231#section-6.5.8"
            : status == StatusCodes.Status404NotFound ? "https://tools.ietf.org/html/rfc7231#section-6.5.4"
            : "https://tools.ietf.org/html/rfc7231#section-6.5.1",
        title = !isClientError ? "An unexpected error occurred."
            : dbStatus is not null ? DatabaseErrors.Describe(error!)
            : tenantViolation is { IsReference: true } ? "The request references a record that does not exist."
            : tenantViolation is not null ? "Not found."
            : "The request was invalid.",
        status
    };
    await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
}));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "DayGrid API v1"));

    using var scope = app.Services.CreateScope();
    try
    {
        // A system (unfiltered) context: sample rows are inserted unowned, so the bootstrap admin
        // claims them when it confirms its email. No-op once data or any account exists.
        await using var systemDb = new AppDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>());
        SeedData.Seed(systemDb);
    }
    catch (Exception ex)
    {
        // Don't crash local dev if Postgres isn't up yet — log and let the developer retry.
        Log.Warning(ex, "Skipping sample-data seed — database not ready");
    }
}

// Static SPA assets are public and served before authentication/authorization run.
app.UseStaticFiles();

app.UseRouting();
if (corsOrigins is not null)
    app.UseCors(AngularDevCorsPolicy);
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<AntiforgeryValidationMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", utc = DateTimeOffset.UtcNow }))
    .WithTags("Health")
    .AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

app.MapAuthEndpoints();
app.MapAdminEndpoints();

app.MapTodayEndpoints();
app.MapTasksEndpoints();
app.MapChecklistEndpoints();
app.MapTimetableEndpoints();
app.MapFutureTaskEndpoints();
app.MapSettingsEndpoints();
app.MapExpensesEndpoints();

app.MapHub<ScheduleHub>("/hubs/schedule");

// Single-origin deployment: the Angular production build lives in wwwroot (populated by the
// BuildAndCopyAngular / CopyAngularDist targets in DayGrid.Api.csproj on publish — see README's
// "Publishing as a single executable" section). UseStaticFiles serves the built JS/CSS/assets;
// MapFallbackToFile lets Angular's client-side router handle deep links like /checklists/{id}
// by always falling back to index.html for any GET that isn't a real file or a mapped API/hub
// route above. Harmless no-op in local dev, where the SPA is served separately by `ng serve`.
// (UseStaticFiles itself runs earlier in the pipeline, before authentication.)
// Unknown /api/* URLs are API misses, not client-side routes: answer 404 instead of letting
// the SPA fallback below return index.html with a 200 (which the Angular HttpClient would
// then fail to parse as JSON).
// (Still behind the fallback policy: anonymous callers get 401 like every other /api URL.)
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html").AllowAnonymous();

if (embeddedPg is not null)
{
    // ApplicationStopping covers a normal shutdown (Ctrl+C, closing the console window, a
    // Windows Service stop request). AppDomain.ProcessExit is a second, best-effort hook for
    // paths that skip the host's graceful-shutdown sequence (e.g. an unhandled exception during
    // startup after the embedded server is already up) — there's no hook that can catch an
    // outright TerminateProcess/"End task", since nothing in-process runs at that point.
    var pgToDispose = embeddedPg;
    app.Lifetime.ApplicationStopping.Register(() => pgToDispose.Dispose());
    AppDomain.CurrentDomain.ProcessExit += (_, _) => pgToDispose.Dispose();
}

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
