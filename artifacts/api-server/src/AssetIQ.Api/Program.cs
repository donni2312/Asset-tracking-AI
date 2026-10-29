using System.Globalization;
using AssetIQ.Api.Auth;
using AssetIQ.Api.Configuration;
using AssetIQ.Api.Data;
using AssetIQ.Api.Domain;
using AssetIQ.Api.Infrastructure;
using AssetIQ.Api.Services;
using AssetIQ.Api.Services.NlQuery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// wwwroot sits next to the published DLL, whatever directory the app is started from.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
});

// Local development reads the repository's .env (real environment variables win); tests configure everything themselves.
if (!builder.Environment.IsEnvironment("Testing"))
{
    DotEnvFile.AddTo(builder.Configuration, Directory.GetCurrentDirectory());
}

ConfigureLogging(builder);

// PORT (as on Replit and in .env) wins; otherwise ASP.NET Core's own URL settings; otherwise 8080.
builder.WebHost.ConfigureKestrel((context, kestrel) =>
{
    var port = context.Configuration["PORT"];
    if (!string.IsNullOrWhiteSpace(port))
    {
        if (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number is <= 0 or > 65535)
        {
            throw new InvalidOperationException($"Invalid PORT value: \"{port}\"");
        }
        kestrel.ListenAnyIP(number);
    }
    else if (string.IsNullOrEmpty(context.Configuration["urls"]) &&
             string.IsNullOrEmpty(context.Configuration["http_ports"]) &&
             string.IsNullOrEmpty(context.Configuration["https_ports"]))
    {
        kestrel.ListenAnyIP(8080);
    }
});

// Configuration is resolved from the built host, so test hosts can override any value.
builder.Services.AddSingleton(sp => AppConfig.Load(sp.GetRequiredService<IConfiguration>()));
// ASSUMPTION: QuestPDF's Community licence (free below USD 1M annual gross revenue) was approved for now;
// TSL must confirm it qualifies, or buy a commercial licence, before production use.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AppClock>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<BcSyncService>();

// Phase 2: what this request may see; narrowed by the token handler, applied by the DbContext's query filters.
builder.Services.AddScoped<AccessScope>();
builder.Services.AddDbContext<AssetIqDbContext>((sp, options) =>
    options.UseNpgsql(DatabaseUrl.ToConnectionString(RequireDatabaseUrl(sp.GetRequiredService<IConfiguration>()))));

builder.Services.AddScoped<AssetQueries>();
builder.Services.AddScoped<AssetAccess>();
builder.Services.AddScoped<AssetStatusService>();
builder.Services.AddScoped<MovementService>();
builder.Services.AddScoped<InsightService>();
builder.Services.AddScoped<GatePassService>();
builder.Services.AddScoped<RfidScanService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<IntentExecutor>();
builder.Services.AddScoped<NlQueryService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<AuditTrail>();
builder.Services.AddScoped<AccessLog>();
builder.Services.AddScoped<AssetRegistrationService>();
builder.Services.AddScoped<BcNumberService>();
builder.Services.AddSingleton<MockBcFixedAssetClient>();
builder.Services.AddSingleton<IBcFixedAssetClient>(sp => sp.GetRequiredService<MockBcFixedAssetClient>());
builder.Services.AddScoped<AssetIQ.Api.Services.Depreciation.DepreciationService>();
builder.Services.AddScoped<ValuationService>();
builder.Services.AddScoped<UpgradeService>();
builder.Services.AddScoped<CompletenessService>();
builder.Services.AddScoped<AssetCardService>();
builder.Services.AddScoped<FinanceReportService>();
// AU-03: retries Business Central number requests; the tests drive the queue themselves.
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<BcNumberWorker>();
builder.Services.AddHttpClient<OpenAiIntentParser>();

builder.Services
    .AddAuthentication(AuthDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, TokenAuthenticationHandler>(AuthDefaults.Scheme, _ => { });
// Every endpoint needs a signed-in user unless it says [AllowAnonymous] (health check, sign-in).
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().AddRequirements(new RoleRequirement(Roles.Everyone)).Build());
builder.Services.AddSingleton<IAuthorizationPolicyProvider, RolePolicyProvider>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IAuthorizationHandler, RoleHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, JsonAuthorizationResultHandler>();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader().WithExposedHeaders("Content-Disposition")));

// One log line per request: method, path (no query string, no headers), status, duration.
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath |
                            HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
    options.CombineLogs = true;
});

builder.Services
    .AddControllers(options => options.Filters.Add<FinancialRedactionFilter>())
    .AddJsonOptions(options => JsonDefaults.Configure(options.JsonSerializerOptions))
    .ConfigureApiBehaviorOptions(options =>
    {
        options.SuppressMapClientErrors = true;
        // Unreadable JSON bodies get the API's usual error shape instead of a ProblemDetails document.
        options.InvalidModelStateResponseFactory = context =>
        {
            var message = context.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
            return new BadRequestObjectResult(new ErrorResponse(message is null ? "Invalid request body" : $"Invalid request body: {message}"));
        };
    });

var app = builder.Build();

RequireDatabaseUrl(app.Configuration);
var config = app.Services.GetRequiredService<AppConfig>();
if (!config.Auth.SecretConfigured)
{
    app.Logger.LogWarning("AUTH_SECRET is not set (or shorter than 16 characters): sign-ins will not survive a restart. See .env.example.");
}

// A published build (`pnpm build:azure`) ships the dashboard in wwwroot, so one App Service serves both.
// Development, tests and Replit (which serves the dashboard itself) have no wwwroot and skip this.
var servesDashboard = app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists;

app.UseHttpLogging();
if (servesDashboard)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}
app.UseRouting();
app.UseMiddleware<ApiErrorMiddleware>();
app.UseCors();
app.UseAuthentication();
// A request nobody signed in to sees no assets, whatever the endpoint.
app.Use(async (context, next) =>
{
    if (context.User.GetAuthUser() is null) context.RequestServices.GetRequiredService<AccessScope>().DenyAll();
    await next(context);
});
app.UseAuthorization();
// The System Administrator may do everything; each of their requests is recorded in the access log.
app.Use(async (context, next) =>
{
    var failed = true;
    try
    {
        await next(context);
        failed = false;
    }
    finally
    {
        if (context.User.GetAuthUser() is { Role: Roles.SystemAdmin } admin)
            await context.RequestServices.GetRequiredService<AccessLog>().RequestAsync(admin, context, failed);
    }
});
app.MapControllers();
if (servesDashboard)
{
    // Client-side routes (/assets/12, /gate-passes) load index.html; unknown /api paths still get the API's 404.
    app.MapFallbackToFile("{*path:regex(^(?!api(/|$)).*$)}", "index.html");
}

app.Lifetime.ApplicationStarted.Register(() =>
    app.Logger.LogInformation("Server listening on {Urls} (organisation time zone {TimeZone})", string.Join(", ", app.Urls), config.Timezone));

app.Run();

static string RequireDatabaseUrl(IConfiguration configuration)
{
    var url = configuration["DATABASE_URL"];
    return string.IsNullOrWhiteSpace(url)
        ? throw new InvalidOperationException("DATABASE_URL must be set. Did you forget to provision a database?")
        : url;
}

static void ConfigureLogging(WebApplicationBuilder builder)
{
    // LOG_LEVEL uses the pino names the Node API understood.
    var level = builder.Configuration["LOG_LEVEL"]?.Trim().ToLowerInvariant() switch
    {
        "trace" => LogLevel.Trace,
        "debug" => LogLevel.Debug,
        "info" => LogLevel.Information,
        "warn" => LogLevel.Warning,
        "error" => LogLevel.Error,
        "fatal" => LogLevel.Critical,
        "silent" => LogLevel.None,
        _ => (LogLevel?)null,
    };
    if (level == LogLevel.None)
    {
        builder.Logging.ClearProviders();
        return;
    }
    if (level is not null) builder.Configuration["Logging:LogLevel:Default"] = level.ToString();

    // Readable single lines while developing; JSON in production (as the Node API did with pino).
    if (builder.Environment.IsDevelopment())
    {
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
    }
    else
    {
        builder.Logging.AddJsonConsole();
    }
}

/// <summary>Entry point, visible to the integration tests' WebApplicationFactory.</summary>
public partial class Program;
