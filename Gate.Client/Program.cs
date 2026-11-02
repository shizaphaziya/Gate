using Avalonia;
using Avalonia.Wayland;

namespace Gate.Client;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect();

        if (OperatingSystem.IsLinux())
        {
            builder.UseWaylandWithFallback();
        }

        return builder
            .WithInterFont()
            .LogToTrace();
    }
}
