using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace AgentYattus.Desktop;

/// <summary>
/// macOS : le logo dans le Dock. Le paquet « Agent Yattus.app » le déclare déjà (Info.plist, yattus.icns) ; mais lancé
/// autrement (dotnet run, le binaire seul), le logiciel n'est pas un paquet et macOS lui donne l'icône générique d'un
/// programme. On la remplace donc nous-mêmes, une fois la fenêtre ouverte, par l'API de macOS (NSApplication).
/// </summary>
internal static class MacDock
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    public static readonly Uri Logo = new("avares://AgentYattus.Desktop/Assets/Branding/yattus.png");

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);

    /// <summary>À appeler sur le fil de l'interface. Hors macOS, ou si quelque chose manque, on garde l'icône du système.</summary>
    public static void ShowLogo()
    {
        if (!OperatingSystem.IsMacOS())
            return;
        try
        {
            using var stream = AssetLoader.Open(Logo);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                // [NSData dataWithBytes:length:] copie les octets : ils peuvent être libérés ensuite.
                var data = Send(objc_getClass("NSData"), sel_registerName("dataWithBytes:length:"), pinned.AddrOfPinnedObject(), (nuint)bytes.Length);
                var image = Send(Send(objc_getClass("NSImage"), sel_registerName("alloc")), sel_registerName("initWithData:"), data);
                if (image != IntPtr.Zero)
                    Send(Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication")), sel_registerName("setApplicationIconImage:"), image);
            }
            finally
            {
                pinned.Free();
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or IOException or FileNotFoundException)
        {
            // Un logo absent ne doit pas empêcher le logiciel de démarrer.
        }
    }
}
