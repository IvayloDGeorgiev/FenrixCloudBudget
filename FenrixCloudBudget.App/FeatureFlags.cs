namespace FenrixCloudBudget.App;

/// <summary>App-level feature switches for functionality that is not ready for release.</summary>
public static class FeatureFlags
{
    /// <summary>Enables the Fenrix Source marketing CTA.</summary>
    public static bool FenrixSource { get; } = false;
}
