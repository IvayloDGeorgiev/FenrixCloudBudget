using MudBlazor;

namespace FenrixCloudBudget.App.Services;

/// <summary>
/// Owns the complete visual personality of the app. Each theme combines CSS design tokens
/// with a matching MudBlazor palette so native components and custom surfaces feel coherent.
/// </summary>
public sealed class ThemeService
{
    public sealed record ThemeInfo(
        string Id,
        string Name,
        string Description,
        string Personality,
        bool Dark,
        string Primary,
        string Secondary,
        string Accent);

    public static readonly IReadOnlyList<ThemeInfo> Available =
    [
        new(
            "daybreak",
            "Daybreak",
            "Luminous cloud whites, ink typography and a violet-to-cyan signal gradient.",
            "Airy · optimistic · glass",
            false,
            "#6D5DFB",
            "#00B8D9",
            "#FFB547"),
        new(
            "nebula",
            "Nebula",
            "Deep-space navy with aurora light, translucent layers and electric data accents.",
            "Immersive · cinematic · glass",
            true,
            "#8B7CFF",
            "#33D6C5",
            "#FF7A9E"),
        new(
            "graphite",
            "Graphite",
            "Near-black precision surfaces, restrained blue light and tighter industrial geometry.",
            "Focused · technical · compact",
            true,
            "#68A8FF",
            "#A7F3D0",
            "#FBBF24"),
        new(
            "tide",
            "Tide",
            "Cool mineral tones, ocean blues and softly layered surfaces for long planning sessions.",
            "Calm · editorial · soft",
            false,
            "#087EA4",
            "#14B8A6",
            "#F9735B")
    ];

    private static readonly IReadOnlyDictionary<string, string> LegacyThemeIds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["aurora"] = "daybreak",
            ["midnight"] = "nebula",
            ["slate"] = "graphite",
            ["mint"] = "tide"
        };

    public string CurrentThemeId { get; private set; } = "daybreak";
    public ThemeInfo Current => Available.First(theme => theme.Id == CurrentThemeId);
    public MudTheme MudTheme => BuildMudTheme(Current);

    public event Action? Changed;

    public void SetTheme(string? themeId)
    {
        var resolved = ResolveThemeId(themeId);
        if (CurrentThemeId == resolved)
            return;

        CurrentThemeId = resolved;
        Changed?.Invoke();
    }

    public static string ResolveThemeId(string? themeId)
    {
        if (!string.IsNullOrWhiteSpace(themeId))
        {
            if (LegacyThemeIds.TryGetValue(themeId, out var migrated))
                return migrated;
            if (Available.Any(theme => theme.Id.Equals(themeId, StringComparison.OrdinalIgnoreCase)))
                return themeId.ToLowerInvariant();
        }

        return "daybreak";
    }

    private static MudTheme BuildMudTheme(ThemeInfo theme)
    {
        MudBlazor.Palette palette = theme.Id switch
        {
            "nebula" => new PaletteDark
            {
                Primary = theme.Primary,
                Secondary = theme.Secondary,
                Tertiary = theme.Accent,
                Background = "#080B16",
                Surface = "#11172A",
                AppbarBackground = "#0C1120",
                DrawerBackground = "#0C1120",
                DrawerText = "#E9ECF7",
                TextPrimary = "#F5F7FF",
                TextSecondary = "#9BA7C4",
                Divider = "#29324B",
                ActionDefault = "#B7C0D8",
                Success = "#3DDC97",
                Warning = "#FFBD5A",
                Error = "#FF6B81",
                Info = "#5AB9FF"
            },
            "graphite" => new PaletteDark
            {
                Primary = theme.Primary,
                Secondary = theme.Secondary,
                Tertiary = theme.Accent,
                Background = "#0D0F12",
                Surface = "#15181D",
                AppbarBackground = "#101216",
                DrawerBackground = "#101216",
                DrawerText = "#E8EBEF",
                TextPrimary = "#F4F6F8",
                TextSecondary = "#969DA8",
                Divider = "#2B3038",
                ActionDefault = "#B5BBC4",
                Success = "#70D7A5",
                Warning = "#FBBF24",
                Error = "#FB7185",
                Info = "#68A8FF"
            },
            "tide" => new PaletteLight
            {
                Primary = theme.Primary,
                Secondary = theme.Secondary,
                Tertiary = theme.Accent,
                Background = "#EEF6F7",
                Surface = "#F9FCFC",
                AppbarBackground = "#F9FCFC",
                DrawerBackground = "#12343B",
                DrawerText = "#F4FFFF",
                TextPrimary = "#102F36",
                TextSecondary = "#5B7479",
                Divider = "#CFE0E2",
                ActionDefault = "#456B73",
                Success = "#159B72",
                Warning = "#D99018",
                Error = "#D95762",
                Info = "#087EA4"
            },
            _ => new PaletteLight
            {
                Primary = theme.Primary,
                Secondary = theme.Secondary,
                Tertiary = theme.Accent,
                Background = "#F4F5FB",
                Surface = "#FFFFFF",
                AppbarBackground = "#FFFFFF",
                DrawerBackground = "#11152A",
                DrawerText = "#F6F7FF",
                TextPrimary = "#171A2B",
                TextSecondary = "#68708A",
                Divider = "#E3E5F0",
                ActionDefault = "#596078",
                Success = "#0BAA75",
                Warning = "#DD9419",
                Error = "#E5566C",
                Info = "#1677FF"
            }
        };

        return new MudTheme
        {
            PaletteLight = palette as PaletteLight ?? new PaletteLight(),
            PaletteDark = palette as PaletteDark ?? new PaletteDark(),
            Typography = new Typography
            {
                Default = new DefaultTypography
                {
                    FontFamily = ["Segoe UI Variable", "Segoe UI", "Inter", "sans-serif"],
                    FontSize = "0.875rem",
                    FontWeight = "450",
                    LineHeight = "1.5"
                },
                H1 = new H1Typography
                {
                    FontFamily = ["Segoe UI Variable Display", "Segoe UI", "sans-serif"],
                    FontWeight = "750",
                    LetterSpacing = "-0.045em"
                },
                H2 = new H2Typography
                {
                    FontFamily = ["Segoe UI Variable Display", "Segoe UI", "sans-serif"],
                    FontWeight = "720",
                    LetterSpacing = "-0.035em"
                },
                Button = new ButtonTypography
                {
                    FontFamily = ["Segoe UI Variable", "Segoe UI", "sans-serif"],
                    FontWeight = "650",
                    LetterSpacing = "0.01em",
                    TextTransform = "none"
                }
            },
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = theme.Id == "graphite" ? "8px" : "14px"
            }
        };
    }
}

/// <summary>Lightweight per-circuit UI state (global dashboard filters, etc.).</summary>
public sealed class AppState
{
    public string ProviderFilter { get; set; } = "All";
    public int? ClientFilter { get; set; }
    public int? ProjectFilter { get; set; }
    public DateOnly RangeFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
    public DateOnly RangeTo { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public event Action? FiltersChanged;
    public void NotifyFiltersChanged() => FiltersChanged?.Invoke();
}
