using AgentYattus.Contracts;
using Avalonia.Controls;
using Avalonia.Data.Converters;

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

    public static readonly IValueConverter KeyPlaceholder = new FuncValueConverter<bool, string>(needsKey =>
        needsKey ? "Clé d’API" : "Clé d’API (facultative)");
}
