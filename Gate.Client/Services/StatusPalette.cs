namespace Gate.Client.Services;

/// <summary>Status text colors that keep AA contrast on the active theme's surfaces.</summary>
public static class StatusPalette
{
    private static bool IsDark => ThemeService.CurrentTheme == AppTheme.Dark;

    public static string Ok => IsDark ? "#34D399" : "#047857";
    public static string Error => IsDark ? "#F87171" : "#B91C1C";
    public static string Warn => IsDark ? "#FBBF24" : "#92400E";
    public static string Info => IsDark ? "#60A5FA" : "#1D4ED8";
    public static string Muted => IsDark ? "#9CA3AF" : "#4B5563";
}
