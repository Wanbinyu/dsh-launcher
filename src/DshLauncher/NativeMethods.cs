using System.Runtime.InteropServices;

namespace DshLauncher;

internal static class NativeMethods
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    public static bool AttachToParentConsole()
    {
        if (!AttachConsole(AttachParentProcess))
        {
            // A GUI process may already have inherited a usable console.
            return GetConsoleWindow() != IntPtr.Zero;
        }

        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            Console.SetIn(new StreamReader(Console.OpenStandardInput()));
        }
        catch (IOException)
        {
            return false;
        }

        return true;
    }
}
