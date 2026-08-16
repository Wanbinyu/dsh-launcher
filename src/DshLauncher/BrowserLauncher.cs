using System.Diagnostics;

namespace DshLauncher;

internal static class BrowserLauncher
{
    public static void Open(Uri url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url.AbsoluteUri,
            UseShellExecute = true
        });
    }

    public static void OpenDirectory(string directory)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true
        });
    }
}
