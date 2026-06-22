using System.Threading.RateLimiting;
using FenrixCloudBudget.Api.Endpoints;
using FenrixCloudBudget.Api.Services;
using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Data;
using FenrixCloudBudget.Data.Providers;
using FenrixCloudBudget.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Data: SQLite by default; point at SQL Server via the FENRIX_DB connection string ----
var conn = builder.Configuration.GetConnectionString("FenrixDb");
builder.Services.AddFenrixData(new DataProviderOptions
{
    Mode = string.IsNullOrWhiteSpace(conn) ? DataProviderMode.Sqlite : DataProviderMode.SqlServer,
    SqlitePath = Path.Combine(AppContext.BaseDirectory, "fenrix.api.db"),
    ConnectionString = conn
});

// ---- Shared services (email/notifications) + server OTP ----
builder.Services.AddFenrixServices();
builder.Services.AddScoped<OtpService>();

// ---- Cross-cutting ----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddAuthorization();

// Rate limit OTP requests (protects the 6-digit code flow).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("otp", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(15) }));
});

var app = builder.Build();

// Ensure schema exists on startup.
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<IDataProvider>().InitializeAsync();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseRateLimiter();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "FenrixCloudBudget.Api" }));
app.MapAuthEndpoints();
app.MapSyncEndpoints();

app.Run();
