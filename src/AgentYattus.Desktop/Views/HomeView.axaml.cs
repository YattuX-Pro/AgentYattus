using AgentYattus.Client.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace AgentYattus.Desktop.Views;

public sealed partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        // Le sélecteur de fichiers du système, que le ViewModel ne connaît pas : il reçoit seulement une fonction.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is HomeViewModel vm)
                vm.PickFiles = PickFilesAsync;
        };
        // Glisser des fichiers sur le composeur : ils sont joints à la mission (leçon 115). Un dossier remonte à la fenêtre (leçon 128).
        Composer.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            if (DataContext is not HomeViewModel vm || e.DataTransfer.TryGetFiles() is not { } items)
                return;
            var files = items.Select(i => i.TryGetLocalPath()).OfType<string>().Where(File.Exists).ToList();
            if (files.Count == 0)
                return;
            e.Handled = true;
            await vm.AttachFilesAsync(files);
        });
    }

    private async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return [];
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Joindre des fichiers", AllowMultiple = true });
        return [.. files.Select(f => f.TryGetLocalPath()).OfType<string>()];
    }
}
