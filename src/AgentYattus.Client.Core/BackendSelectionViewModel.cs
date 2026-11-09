using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentYattus.Client.Core;

/// <summary>Un choix de connexion, conservé hors des bases pour pouvoir les retrouver au lancement suivant.</summary>
public sealed record BackendSelection(bool IsLocal, string ApiUrl, string Authority, string AdminUrl)
{
    public static string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentYattus");

    public static string FilePath => Path.Combine(DataFolder, "connection.json");

    public static BackendSelection Defaults => new(
        string.Equals(Environment.GetEnvironmentVariable("AGENTYATTUS_MODE"), "local", StringComparison.OrdinalIgnoreCase),
        Environment.GetEnvironmentVariable("AGENTYATTUS_API_URL") ?? "http://localhost:5080/",
        Environment.GetEnvironmentVariable("AGENTYATTUS_AUTHORITY") ?? "http://localhost:8380/realms/agent-yattus",
        Environment.GetEnvironmentVariable("AGENTYATTUS_ADMIN_URL") ?? "http://localhost:4200/");

    /// <summary>Serveur par défaut, même si l'ancien écran avait mémorisé le mode local.</summary>
    public static BackendSelection ForStartup(string? path = null)
    {
        var defaults = Defaults;
        var saved = defaults;
        try
        {
            if (File.Exists(path ?? FilePath))
                saved = Load(path ?? FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Un ancien fichier illisible ne bloque pas le lancement sur le serveur configuré.
        }
        return new(defaults.IsLocal,
            Environment.GetEnvironmentVariable("AGENTYATTUS_API_URL") ?? (IsValidAddress(saved.ApiUrl) ? saved.ApiUrl : defaults.ApiUrl),
            Environment.GetEnvironmentVariable("AGENTYATTUS_AUTHORITY") ?? (IsValidAddress(saved.Authority) ? saved.Authority : defaults.Authority),
            Environment.GetEnvironmentVariable("AGENTYATTUS_ADMIN_URL") ?? (IsValidAddress(saved.AdminUrl) ? saved.AdminUrl : defaults.AdminUrl));
    }

    public static BackendSelection Load(string path)
    {
        var selection = JsonSerializer.Deserialize<BackendSelection>(File.ReadAllText(path));
        if (selection is null || selection.ApiUrl is null || selection.Authority is null || selection.AdminUrl is null)
            throw new JsonException("Choix de connexion incomplet.");
        return selection;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, path, overwrite: true);
    }

    public static bool IsValidAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}

/// <summary>Le choix est confirmé à chaque lancement ; un échec ne fait jamais basculer vers une autre base.</summary>
public sealed partial class BackendSelectionViewModel : ObservableObject
{
    private readonly string _path;

    public BackendSelectionViewModel(string? path = null)
    {
        _path = path ?? BackendSelection.FilePath;
        var selection = BackendSelection.Defaults;
        try
        {
            if (File.Exists(_path))
                selection = BackendSelection.Load(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Error = "Le dernier choix n'a pas pu être lu. Choisissez la base à utiliser.";
        }
        IsLocal = selection.IsLocal;
        ApiUrl = selection.ApiUrl;
        Authority = selection.Authority;
        AdminUrl = selection.AdminUrl;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsServer))]
    public partial bool IsLocal { get; set; }

    public bool IsServer { get => !IsLocal; set => IsLocal = !value; }

    [ObservableProperty]
    public partial string ApiUrl { get; set; }

    [ObservableProperty]
    public partial string Authority { get; set; }

    [ObservableProperty]
    public partial string AdminUrl { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public string LocalDatabase => Path.Combine(
        Environment.GetEnvironmentVariable("Local__DataFolder") ?? BackendSelection.DataFolder, "agent-yattus.db");

    public event Action<BackendSelection>? Selected;

    [RelayCommand]
    private void Continue()
    {
        Error = null;
        if (!IsLocal && new[] { ApiUrl, Authority, AdminUrl }.Any(address => !BackendSelection.IsValidAddress(address)))
        {
            Error = "Indiquez des adresses HTTPS complètes (HTTP est accepté sur cet ordinateur), sans identifiant ni paramètres.";
            return;
        }
        var selection = new BackendSelection(IsLocal, ApiUrl.Trim().TrimEnd('/') + "/", Authority.Trim().TrimEnd('/'), AdminUrl.Trim());
        try
        {
            selection.Save(_path);
            Selected?.Invoke(selection);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = "Le choix n'a pas pu être enregistré : vérifiez l'accès au dossier de données.";
        }
    }
}
