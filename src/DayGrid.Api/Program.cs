using System.Text.Json;
using System.Text.Json.Serialization;
using DayGrid.Api;
using DayGrid.Api.Endpoints;
using DayGrid.Api.Hubs;
using DayGrid.Application.Scheduling;
using DayGrid.Application.Time;
using DayGrid.Infrastructure.BackgroundServices;
using DayGrid.Infrastructure.Data;
using DayGrid.Infrastructure.Email;
using DayGrid.Infrastructure.Scheduling;
using DayGrid.Infrastructure.Time;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
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

    var dataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DayGrid", "pgdata");
    var schemaSqlPath = Path.Combine(AppContext.BaseDirectory, "db", "init.sql");

    (embeddedPg, connectionString) = await EmbeddedDatabase.StartAsync(dataDir, schemaSqlPath);
}
else
{
    connectionString =
        builder.Configuration.GetConnectionString("Default")
        ?? "Host=localhost;Database=daygrid;Username=daygrid;Password=dev";
}

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
const string AngularDevCorsPolicy = "AngularDev";
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() is { Length: > 0 } configuredOrigins
    ? configuredOrigins
    : new[] { "http://localhost:4200" };
builder.Services.AddCors(options => options.AddPolicy(AngularDevCorsPolicy, policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// ---------------------------------------------------------------------
// SignalR
// ---------------------------------------------------------------------
builder.Services.AddSignalR();

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
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "DayGrid API",
        Version = "v1",
        Description = "Personal daily checklist & timetable API."
    });
});

// ---------------------------------------------------------------------
// Email
// ---------------------------------------------------------------------
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
builder.Services.AddScoped<IEmailSender, MailKitEmailSender>();

// ---------------------------------------------------------------------
// Application services
// ---------------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAppClock>(sp => new AppClock(
    sp.GetRequiredService<TimeProvider>(),
    builder.Configuration["App:TimeZone"] ?? AppClock.DefaultTimeZoneId));
builder.Services.AddScoped<IDayPlanBuilder, DayPlanBuilder>();

// ---------------------------------------------------------------------
// Background services
// ---------------------------------------------------------------------
builder.Services.AddHostedService<ReminderDispatcherService>();
builder.Services.AddHostedService<DailyDigestService>();

var app = builder.Build();

// Resolve the clock eagerly so a bad App:TimeZone fails at startup, not on the first request.
var appClock = app.Services.GetRequiredService<IAppClock>();
app.Logger.LogInformation("User time zone: {TimeZone}; local time now {Now:yyyy-MM-dd HH:mm zzz}", appClock.TimeZone.Id, appClock.Now);

// ---------------------------------------------------------------------
// Schema bootstrap for External mode (e.g. a fresh Azure Database for PostgreSQL). There are no
// EF migrations; db/init.sql is the schema. Opt-in via Database:InitializeSchema=true — guarded
// by an existence check, so it only ever runs against an empty database. Never seeds data.
// ---------------------------------------------------------------------
if (embeddedPg is null && app.Configuration.GetValue<bool>("Database:InitializeSchema"))
{
    var schemaSqlPath = Path.Combine(AppContext.BaseDirectory, "db", "init.sql");
    const int maxAttempts = 5;
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            await SchemaBootstrapper.EnsureSchemaAsync(connectionString, schemaSqlPath, app.Logger);
            break;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            app.Logger.LogWarning(ex, "Schema bootstrap attempt {Attempt}/{MaxAttempts} failed — retrying", attempt, maxAttempts);
            await Task.Delay(TimeSpan.FromSeconds(5 * attempt));
        }
        catch (Exception ex)
        {
            // Keep the host up so /health and logs stay reachable; /health/ready reports the DB state.
            app.Logger.LogError(ex, "Schema bootstrap failed after {MaxAttempts} attempts — database may be missing its schema", maxAttempts);
        }
    }
}

app.UseForwardedHeaders();

app.UseSerilogRequestLogging();

// ---------------------------------------------------------------------
// ProblemDetails-shaped error handling for everything unhandled.
// ---------------------------------------------------------------------
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    // Malformed request bodies/route values surface as BadHttpRequestException (thrown in
    // Development) — those are client errors, not 500s.
    var badRequest = context.Features.Get<IExceptionHandlerFeature>()?.Error as BadHttpRequestException;
    var status = badRequest?.StatusCode ?? StatusCodes.Status500InternalServerError;
    context.Response.StatusCode = status;
    context.Response.ContentType = "application/problem+json";
    var problem = new
    {
        type = badRequest is null ? "https://tools.ietf.org/html/rfc7231#section-6.6.1" : "https://tools.ietf.org/html/rfc7231#section-6.5.1",
        title = badRequest is null ? "An unexpected error occurred." : "The request was invalid.",
        status
    };
    await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
}));

app.UseCors(AngularDevCorsPolicy);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "DayGrid API v1"));

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        db.Database.Migrate();
        SeedData.Seed(db); // no-op if the DB already has data — see SeedData.Seed's guard clause
    }
    catch (Exception ex)
    {
        // Don't crash local dev if Postgres isn't up yet / migrations aren't generated yet —
        // log and let the developer retry once the DB is ready.
        Log.Warning(ex, "Skipping auto-migrate/seed — database not ready");
    }
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy", utc = DateTimeOffset.UtcNow }))
    .WithTags("Health");
app.MapHealthChecks("/health/ready");

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
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

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
