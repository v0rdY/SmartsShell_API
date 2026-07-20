using Avalonia;

namespace SmartShellCleanerAvalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var singleInstance = new Mutex(true, @"Local\SmartShellCleanerAvalonia", out var isFirstInstance);
        if (!isFirstInstance)
            return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
