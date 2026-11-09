using AgentYattus.Client.Core;

namespace AgentYattus.Mobile;

public static class MauiProgram
{
    // Même adresse que le desktop. Émulateur Android : `adb reverse tcp:5080 tcp:5080` pour joindre l'API du Mac
    // (le simulateur iOS partage déjà la boucle locale), ce qui garde l'API sur localhost.
    private static readonly Uri ApiUrl = new("http://localhost:5080/");

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        // Champ nu, comme la maquette : la bordure est portée par le composer.
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("SansBordure", (handler, _) =>
        {
#if IOS
            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#elif ANDROID
            handler.PlatformView.Background = null;
#endif
        });
        builder.Services.AddSingleton(new HomeViewModel(new ApiClient(new HttpClient { BaseAddress = ApiUrl })));
        builder.Services.AddSingleton(_ => new MissionsHubClient(ApiUrl));
        return builder.Build();
    }
}
