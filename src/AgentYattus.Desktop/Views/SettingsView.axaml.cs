using AgentYattus.Contracts;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AgentYattus.Desktop.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
}

/// <summary>Les petits textes de l'écran, calculés depuis l'état.</summary>
public static class SettingsTexts
{
    public static readonly IValueConverter KindLabel = new FuncValueConverter<LlmConnectionKind, string>(kind => kind switch
    {
        LlmConnectionKind.ApiKey => "clé d’API",
        LlmConnectionKind.Local => "local",
        _ => "adresse au choix",
    });

    /// <summary>Un modèle propre à l'agent ressort en vert ; « Par défaut » reste discret.</summary>
    public static readonly IValueConverter OwnBrush = new FuncValueConverter<bool, IBrush>(own =>
        own ? new SolidColorBrush(Color.Parse("#5E9E92")) : new SolidColorBrush(Color.Parse("#938E83")));

    public static readonly IValueConverter KeyPlaceholder = new FuncValueConverter<bool, string>(needsKey =>
        needsKey ? "Clé d’API" : "Clé d’API (facultative)");
}
