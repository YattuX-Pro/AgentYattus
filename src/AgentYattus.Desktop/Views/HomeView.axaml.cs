using AgentYattus.Client.Core;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AgentYattus.Desktop.Views;

public sealed partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        // Le sélecteur de dossiers du système, que le ViewModel ne connaît pas : il reçoit seulement une fonction.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is HomeViewModel vm)
                vm.PickFolder = PickFolderAsync;
        };
    }

    private async Task<string?> PickFolderAsync(string? start)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return null;
        var options = new FolderPickerOpenOptions { Title = "Le dossier où les agents travaillent", AllowMultiple = false };
        if (start is not null && await storage.TryGetFolderFromPathAsync(start) is { } folder)
            options.SuggestedStartLocation = folder;
        var picked = await storage.OpenFolderPickerAsync(options);
        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }
}
