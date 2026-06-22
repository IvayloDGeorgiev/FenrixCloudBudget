namespace FenrixCloudBudget.Core.Enums;

/// <summary>Supported cloud providers.</summary>
public enum CloudProvider
{
    Aws = 0,
    Azure = 1,
    Gcp = 2
}

/// <summary>Where a Service's data originated.</summary>
public enum ServiceSource
{
    Manual = 0,
    Connected = 1
}

/// <summary>Budget reset period.</summary>
public enum BudgetPeriod
{
    Monthly = 0,
    Quarterly = 1,
    Annual = 2
}

/// <summary>Backend storage mode (Settings -> Data &amp; Connections).</summary>
public enum DataProviderMode
{
    Sqlite = 0,
    SqlServer = 1,
    CloudSql = 2,
    Saas = 3
}

/// <summary>Reminder category.</summary>
public enum ReminderType
{
    ClientSecret = 0,
    Certificate = 1,
    Custom = 2
}

public enum ReminderStatus
{
    Active = 0,
    Snoozed = 1,
    Dismissed = 2,
    Done = 3
}

/// <summary>Channel an alert/reminder is delivered through.</summary>
[Flags]
public enum NotificationChannel
{
    None = 0,
    InApp = 1,
    LocalDevice = 2,
    Email = 4
}

public enum DeliveryResult
{
    Sent = 0,
    Failed = 1,
    Suppressed = 2
}

public enum NotificationSourceType
{
    Budget = 0,
    Reminder = 1,
    Otp = 2,
    Invitation = 3
}

/// <summary>Selected outbound email transport (Settings -> Email &amp; Notifications).</summary>
public enum EmailMethod
{
    InAppAndDeviceOnly = 0,
    CustomSmtp = 1,
    Resend = 2,
    SendGrid = 3,
    AmazonSes = 4,
    Postmark = 5,
    AzureCommunicationServices = 6,
    FenrixCloudManaged = 7
}

public enum EmailConfigStatus
{
    NotConfigured = 0,
    Verified = 1,
    Failed = 2
}

public enum UserRole
{
    Admin = 0,
    Member = 1
}

public enum UserStatus
{
    Invited = 0,
    Active = 1,
    Disabled = 2
}

public enum InvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Expired = 2,
    Revoked = 3
}

public enum ProjectStatus
{
    Active = 0,
    Paused = 1,
    Archived = 2
}

public enum AuthMode
{
    LocalNone = 0,
    LocalPin = 1,
    EmailOtp = 2,
    B2CAzureEntra = 3,
    B2CAwsCognito = 4
}
