using System.Runtime.InteropServices;

namespace AgentYattus.Desktop;

internal static class ReducedMotion
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern byte GetBool(IntPtr receiver, IntPtr selector);
    public static bool Enabled
    {
        get
        {
            if (Environment.GetEnvironmentVariable("AGENTYATTUS_REDUCED_MOTION") == "1") return true;
            if (!OperatingSystem.IsMacOS()) return false;
            var workspace = Send(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
            return workspace != IntPtr.Zero && GetBool(workspace, sel_registerName("accessibilityDisplayShouldReduceMotion")) != 0;
        }
    }
}
