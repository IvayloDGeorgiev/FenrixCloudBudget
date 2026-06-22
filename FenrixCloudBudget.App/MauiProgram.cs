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
        builder.Services.AddFenrixAlertScheduler(TimeSpan.FromHours(6));

        // Cloud connection orchestration (needs the cloud connector factory from AddFenrixCloud above).
        builder.Services.AddScoped<FenrixCloudBudget.Services.Cloud.CloudConnectionService>();

        // ---- Platform channel implementations (override the no-op/dev defaults) ----
        builder.Services.AddSingleton<ISecureKeyProvider, MauiSecureKeyProvider>();
        builder.Services.AddSingleton<IInAppNotifier, MauiInAppNotifier>();
        builder.Services.AddSingleton<ILocalNotifier, MauiLocalNotifier>();

        // ---- App-level UI state ----
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddScoped<AppState>();

        var app = builder.Build();

        // Ensure DB exists/migrated and seeded before first render, then start the alert loop.
        // (MAUI does not auto-start IHostedService, so start the scheduler explicitly.)
        Task.Run(async () =>
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IDataProvider>().InitializeAsync();
            await app.Services.GetRequiredService<FenrixCloudBudget.Services.Sync.AlertSchedulerService>()
                .StartAsync(CancellationToken.None);
        });

        return app;
    }
}
