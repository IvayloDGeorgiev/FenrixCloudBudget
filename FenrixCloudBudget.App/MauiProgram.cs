using FenrixCloudBudget.App.Services;
using FenrixCloudBudget.Cloud;
using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Data;
using FenrixCloudBudget.Data.Providers;
using FenrixCloudBudget.Services;
using FenrixCloudBudget.Services.Notifications;
using FenrixCloudBudget.Services.Security;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using Plugin.LocalNotification;

namespace FenrixCloudBudget.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseLocalNotification();
        // Fonts: the Blazor UI uses system fonts via CSS (see wwwroot/css). Add MAUI
        // .ttf fonts here later if native MAUI controls need a custom typeface.

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddMudServices();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // ---- Data: SQLite by default in the app's data directory ----
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "fenrix.db");
        builder.Services.AddFenrixData(new DataProviderOptions
        {
            Mode = Core.Enums.DataProviderMode.Sqlite,
            SqlitePath = dbPath
        });

        // ---- Cross-cutting services ----
        builder.Services.AddFenrixServices();
        builder.Services.AddFenrixCloud();
        builder.Services.AddScoped<FenrixCloudBudget.Services.Cloud.CloudConnectionService>();
        builder.Services.AddScoped<FenrixCloudBudget.Services.Sync.CostSyncService>();
        builder.Services.AddFenrixAlertScheduler(TimeSpan.FromHours(6));

        // ---- Platform channel implementations (override the no-op/dev defaults) ----
        builder.Services.AddSingleton<ISecureKeyProvider, MauiSecureKeyProvider>();
        builder.Services.AddSingleton<IInAppNotifier, MauiInAppNotifier>();
        builder.Services.AddSingleton<ILocalNotifier, MauiLocalNotifier>();

        // ---- App-level UI state ----
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddScoped<AuthSessionService>();
        builder.Services.AddScoped<AppState>();

        var app = builder.Build();

        // Ensure migrations and the bootstrap administrator exist before Blazor can render
        // the login form. MAUI does not auto-start IHostedService, so the scheduler follows
        // in the background after deterministic database initialization.
        using (var scope = app.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<IDataProvider>()
                .InitializeAsync()
                .GetAwaiter()
                .GetResult();

        Task.Run(() => app.Services
            .GetRequiredService<FenrixCloudBudget.Services.Sync.AlertSchedulerService>()
            .StartAsync(CancellationToken.None));

        return app;
    }
}
