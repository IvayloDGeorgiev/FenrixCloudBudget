using FenrixCloudBudget.Core.Enums;
using FenrixCloudBudget.Core.Interfaces;

namespace FenrixCloudBudget.Services.Email;

/// <summary>
/// Resolves the active IEmailSender by method. New providers register themselves as an
/// IEmailSender in DI; this factory just indexes them — no UI or factory rewrite per provider.
/// </summary>
public sealed class EmailSenderFactory : IEmailSenderFactory
{
    private readonly IReadOnlyDictionary<EmailMethod, IEmailSender> _byMethod;

    public EmailSenderFactory(IEnumerable<IEmailSender> senders)
        => _byMethod = senders.ToDictionary(s => s.Method);

    public IEmailSender Resolve(EmailMethod method)
        => _byMethod.TryGetValue(method, out var sender)
            ? sender
            : _byMethod[EmailMethod.InAppAndDeviceOnly];

    public IEnumerable<IEmailSender> All => _byMethod.Values;
}
