using Android.App;
using Android.Runtime;

namespace AgentYattus.Mobile;

// HTTP en clair seulement en Debug, pour l'API locale ; une build Release exige HTTPS.
#if DEBUG
[Application(UsesCleartextTraffic = true)]
#else
[Application]
#endif
public class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
