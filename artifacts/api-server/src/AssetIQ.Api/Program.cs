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

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AppClock>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<BcSyncService>();

builder.Services.AddDbContext<AssetIqDbContext>((sp, options) =>
    options.UseNpgsql(DatabaseUrl.ToConnectionString(RequireDatabaseUrl(sp.GetRequiredService<IConfiguration>()))));

builder.Services.AddScoped<AssetQueries>();
builder.Services.AddScoped<MovementService>();
builder.Services.AddScoped<InsightService>();
builder.Services.AddScoped<GatePassService>();
builder.Services.AddScoped<RfidScanService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<IntentExecutor>();
builder.Services.AddScoped<NlQueryService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddHttpClient<OpenAiIntentParser>();

builder.Services
    .AddAuthentication(AuthDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, TokenAuthenticationHandler>(AuthDefaults.Scheme, _ => { });
builder.Services.AddAuthorization(options =>
{
    foreach (var role in Roles.All)
    {
        options.AddPolicy(RequireRoleAttribute.PolicyFor(role), policy => policy.AddRequirements(new MinimumRoleRequirement(role)));
    }
});
builder.Services.AddSingleton<IAuthorizationHandler, MinimumRoleHandler>();
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
    .AddControllers()
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

app.UseHttpLogging();
app.UseRouting();
app.UseMiddleware<ApiErrorMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

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
