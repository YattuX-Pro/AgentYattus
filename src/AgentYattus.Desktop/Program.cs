using Avalonia;

namespace AgentYattus.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Le logiciel parle français, dates comprises (« samedi 7 novembre »), quelle que soit la langue du système.
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.DefaultThreadCurrentUICulture =
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.CurrentUICulture = new("fr-FR");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Utilisé aussi par le concepteur visuel de l'IDE.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
