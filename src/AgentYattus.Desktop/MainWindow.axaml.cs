using AgentYattus.Client.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace AgentYattus.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly Avalonia.Threading.DispatcherTimer _conversationClock = new() { Interval = TimeSpan.FromSeconds(12) };
    public MainWindow()
    {
        InitializeComponent();
        // Le logo dans le Dock, même lancé sans le paquet .app (dotnet run) : voir MacDock.
        Opened += (_, _) => { MacDock.ShowLogo(); _conversationClock.Start(); };
        _conversationClock.Tick += async (_, _) => { if (DataContext is ShellViewModel shell) await shell.RefreshConversationsAsync(); };
        Closed += (_, _) => _conversationClock.Stop();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ShellViewModel shell) shell.LoggedIn += () => _ = shell.RefreshConversationsAsync();
        };
        // Un dossier glissé n'importe où sur la fenêtre devient le dossier du projet (leçon 128) ; des fichiers, eux,
        // se déposent sur le compositeur (leçon 115), qui les prend avant nous.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) =>
        {
            if (HomeOnScreen is { } home)
                home.DraggedFolder = null;
        });
        AddHandler(DragDrop.DropEvent, OnDropAsync);
    }

    /// <summary>L'accueil, s'il est à l'écran et que l'on peut y choisir le dossier du projet.</summary>
    private HomeViewModel? HomeOnScreen => DataContext is ShellViewModel { Current: HomeViewModel home } && home.CanChooseFolder ? home : null;

    /// <summary>Un seul élément glissé, et c'est un dossier de cet ordinateur.</summary>
    private static string? SingleFolder(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles() is [var item] && item.TryGetLocalPath() is { } path && Directory.Exists(path) ? path : null;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (HomeOnScreen is not { } home || SingleFolder(e) is not { } folder)
            return;
        home.DraggedFolder = folder;
        e.DragEffects = DragDropEffects.Copy;
    }

    private async void OnDropAsync(object? sender, DragEventArgs e)
    {
        if (HomeOnScreen is not { } home)
            return;
        var folder = home.DraggedFolder ?? SingleFolder(e);
        home.DraggedFolder = null;
        if (folder is null || e.Handled)
            return;
        e.Handled = true;
        await home.SetFolderAsync(folder);
    }

    /// <summary>Le panneau des missions se referme dès qu'on a cliqué une ligne.</summary>
    private void CloseActivity(object? sender, RoutedEventArgs e) => ActivityButton.Flyout?.Hide();
}
