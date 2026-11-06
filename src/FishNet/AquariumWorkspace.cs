using System.Runtime.InteropServices;

[assembly: System.Reflection.AssemblyMetadata("AquariumWorkspace", "1")]

namespace FishNet;

internal static class AquariumWorkspace
{
    internal static bool Enabled => Environment.GetEnvironmentVariable("AQUARIUM_HOSTED") == "1";
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetProp(nint window, string name, nint value);
    internal static void Mark(Microsoft.UI.Xaml.Window window)
    {
        if (Enabled && !SetProp(WinRT.Interop.WindowNative.GetWindowHandle(window), "Aquarium.Workspace.v1", 1))
            throw new InvalidOperationException("Could not register the Aquarium workspace.");
    }
}
