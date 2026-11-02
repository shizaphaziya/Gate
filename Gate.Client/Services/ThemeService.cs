using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Gate.Client.Services;

public enum AppTheme
{
    Dark,
    Light,
    Pink
}

public static class ThemeService
{
    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public static void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        if (Application.Current == null) return;

        Application.Current.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Pink => ThemeVariant.Light,
            _ => ThemeVariant.Dark
        };

        var res = Application.Current.Resources;

        switch (theme)
        {
            case AppTheme.Light:
                SetBrush(res, "GateBgApp", "#F3F4F6");
                SetBrush(res, "GateBgHeader", "#FFFFFF");
                SetBrush(res, "GateBgCard", "#FFFFFF");
                SetBrush(res, "GateBgCardSecondary", "#F9FAFB");
                SetBrush(res, "GateBgInput", "#FFFFFF");
                SetBrush(res, "GateBorderBrush", "#E5E7EB");
                SetBrush(res, "GateTextPrimary", "#111827");
                SetBrush(res, "GateTextSecondary", "#4B5563");
                SetBrush(res, "GateTextTertiary", "#9CA3AF");
                SetBrush(res, "GateAccent", "#2563EB");
                SetBrush(res, "GateAccentFg", "#FFFFFF");
                SetBrush(res, "GateBadgeBg", "#E5E7EB");
                SetBrush(res, "GateButtonNeutralBg", "#E5E7EB");
                SetBrush(res, "GateAuditLogBg", "#FFFFFF");
                SetBrush(res, "GateAuditRowBg", "#F9FAFB");
                SetBrush(res, "GateAuditHeaderBg", "#E5E7EB");
                SetBrush(res, "GateTerminalBg", "#1E1E24");
                SetBrush(res, "GateTerminalFg", "#58A6FF");
                SetBrush(res, "GateTerminalLogsFg", "#E5E7EB");
                SetBrush(res, "GateSelectionBg", "#DBEAFE");
                break;

            case AppTheme.Pink:
                // Cute Pink Theme with Cat Paws and high contrast WCAG AAA plum text (:3 🐾)
                SetBrush(res, "GateBgApp", "#FFF0F5");            // LavenderBlush
                SetBrush(res, "GateBgHeader", "#FFE4EC");         // Soft pastel rose header
                SetBrush(res, "GateBgCard", "#FFFFFF");           // Crisp white card
                SetBrush(res, "GateBgCardSecondary", "#FFF5F8");  // Subtle pinkish
                SetBrush(res, "GateBgInput", "#FFFFFF");
                SetBrush(res, "GateBorderBrush", "#F8BBD0");      // Rose border
                SetBrush(res, "GateTextPrimary", "#4A1525");      // Deep plum berry (contrast > 12:1)
                SetBrush(res, "GateTextSecondary", "#88304E");    // Rose berry (contrast > 6:1)
                SetBrush(res, "GateTextTertiary", "#AD5374");
                SetBrush(res, "GateAccent", "#FF4081");           // Vibrant pink accent
                SetBrush(res, "GateAccentFg", "#FFFFFF");
                SetBrush(res, "GateBadgeBg", "#FFD1DC");          // Pastel paw badge
                SetBrush(res, "GateButtonNeutralBg", "#FFD6E0");
                SetBrush(res, "GateAuditLogBg", "#FFFFFF");
                SetBrush(res, "GateAuditRowBg", "#FFF5F8");
                SetBrush(res, "GateAuditHeaderBg", "#FFD1DC");
                SetBrush(res, "GateTerminalBg", "#26131D");       // Cute dark berry terminal
                SetBrush(res, "GateTerminalFg", "#FF80AB");       // Bright bubblegum text
                SetBrush(res, "GateTerminalLogsFg", "#FFD1DC");   // Soft rose logs text
                SetBrush(res, "GateSelectionBg", "#FCE4EC");
                break;

            case AppTheme.Dark:
            default:
                SetBrush(res, "GateBgApp", "#121316");
                SetBrush(res, "GateBgHeader", "#1B1C22");
                SetBrush(res, "GateBgCard", "#1B1C22");
                SetBrush(res, "GateBgCardSecondary", "#22242D");
                SetBrush(res, "GateBgInput", "#22242D");
                SetBrush(res, "GateBorderBrush", "#2B2D3A");
                SetBrush(res, "GateTextPrimary", "#F3F4F6");
                SetBrush(res, "GateTextSecondary", "#9CA3AF");
                SetBrush(res, "GateTextTertiary", "#6B7280");
                SetBrush(res, "GateAccent", "#2563EB");
                SetBrush(res, "GateAccentFg", "#FFFFFF");
                SetBrush(res, "GateBadgeBg", "#262833");
                SetBrush(res, "GateButtonNeutralBg", "#374151");
                SetBrush(res, "GateAuditLogBg", "#1B1C22");
                SetBrush(res, "GateAuditRowBg", "#22242D");
                SetBrush(res, "GateAuditHeaderBg", "#121316");
                SetBrush(res, "GateTerminalBg", "#0D1117");
                SetBrush(res, "GateTerminalFg", "#58A6FF");
                SetBrush(res, "GateTerminalLogsFg", "#C9D1D9");
                SetBrush(res, "GateSelectionBg", "#1E293B");
                break;
        }
    }

    private static void SetBrush(IResourceDictionary res, string key, string hexColor)
    {
        res[key] = new SolidColorBrush(Color.Parse(hexColor));
    }
}
