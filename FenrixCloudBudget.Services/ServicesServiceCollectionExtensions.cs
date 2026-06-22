using FenrixCloudBudget.Core.Interfaces;
using FenrixCloudBudget.Services.Auth;
using FenrixCloudBudget.Services.Email;
using FenrixCloudBudget.Services.Email.Adapters;
using FenrixCloudBudget.Services.Email.Templates;
using FenrixCloudBudget.Services.Notifications;
using FenrixCloudBudget.Services.Security;
using FenrixCloudBudget.Services.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FenrixCloudBudget.Services;

public static class ServicesServiceCollectionExtensions
{
    /// <summary>
    /// Registers security, email, notification and sync services. The host (MAUI / API)
    /// supplies platform channel implementations (IInAppNotifier, ILocalNotifier,
    /// ISecureKeyProvider) by calling the matching Use* helpers, otherwise no-op/dev defaults apply.
    /// </summary>
    public static IServiceCollection AddFenrixServices(this IServiceCollection services)
    {
        services.AddHttpClient();

        // Security — default to the dev/file key provider; MAUI host overrides with SecureStorage.
        services.TryAddSingleton<ISecureKeyProvider, DevFileKeyProvider>();
        services.AddSingleton<ISecretStore, AesSecretStore>();

        // Email adapters (every IEmailSender is discovered by the factory).
        services.AddScoped<EmailAdapterContext>();
        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();
        services.AddScoped<IEmailSender, InAppOnlyEmailSender>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IEmailSender, ResendEmailSender>();
        services.AddScoped<IEmailSender, SendGridEmailSender>();
        services.AddScoped<IEmailSender, PostmarkEmailSender>();
        services.AddScoped<IEmailSender, SesEmailSender>();
        services.AddScoped<IEmailSender, AzureCommunicationEmailSender>();
        services.AddScoped<IEmailSender, FenrixManagedEmailSender>();
        services.AddScoped<IEmailSenderFactory, EmailSenderFactory>();

        // Notification channels — no-op defaults; MAUI host overrides.
        services.TryAddSingleton<IInAppNotifier, NullInAppNotifier>();
        services.TryAddSingleton<ILocalNotifier, NullLocalNotifier>();
        services.AddScoped<INotificationService, NotificationService>();

        // Sync / evaluation.
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<LocalPasswordAuthenticationService>();
        services.AddScoped<UserInvitationService>();
        services.AddScoped<BudgetEvaluator>();
        services.AddScoped<ReminderEvaluator>();

        return services;
    }

    /// <summary>Adds the background alert scheduler (budget + reminder evaluation on an interval).</summary>
    public static IServiceCollection AddFenrixAlertScheduler(this IServiceCollection services, TimeSpan? interval = null)
    {
        services.AddSingleton<AlertSchedulerService>(sp =>
            new AlertSchedulerService(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AlertSchedulerService>>(),
                interval));
        services.AddHostedService(sp => sp.GetRequiredService<AlertSchedulerService>());
        return services;
    }
}
