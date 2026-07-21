using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Semi.Avalonia;
using SukiUI;
using Application = Avalonia.Application;

namespace SmartShellCleanerAvalonia;

public sealed class App : Application
{
    public override void Initialize()
    {
        SetControlTheme(UiLabAppearance.Current.Library);
    }

    private void SetControlTheme(string library)
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Clear();
        try
        {
            Styles.Add(library switch
            {
                "Semi" => new SemiTheme(),
                "SukiUI" => new SukiTheme(),
                "Material" => CreateMaterialTheme(),
                _ => new FluentTheme()
            });
        }
        catch
        {
            Styles.Add(new FluentTheme());
        }

        Styles.Add(new StyleInclude(new Uri("avares://SmartShellCleaner.Avalonia/"))
        {
            Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml")
        });
    }

    private static IStyle CreateMaterialTheme()
    {
        var type = Type.GetType("Material.Styles.Themes.MaterialTheme, Material.Styles");
        return type != null && Activator.CreateInstance(type) is IStyle theme
            ? theme
            : new FluentTheme();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
