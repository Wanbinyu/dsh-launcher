namespace DshLauncher;

internal static class ManagedHarnessPaths
{
    public static string GetRoot()
    {
        var configured = Environment.GetEnvironmentVariable("DSH_LAUNCHER_MANAGED_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured.Trim());
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "dsh-launcher",
            "managed-harness");
    }

    public static string GetPackageEntry()
    {
        return Path.Combine(GetRoot(), "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
    }

    public static string GetPackageJson()
    {
        return Path.Combine(GetRoot(), "node_modules", "@deepseek-ai", "dsh", "package.json");
    }

    public static string GetToolsRoot()
    {
        return Path.Combine(GetRoot(), ".tools");
    }

    public static string GetPnpmEntry()
    {
        return Path.Combine(GetToolsRoot(), "node_modules", "pnpm", "bin", "pnpm.cjs");
    }

    public static string GetPnpmPackageJson()
    {
        return Path.Combine(GetToolsRoot(), "node_modules", "pnpm", "package.json");
    }

    public static string GetMarker()
    {
        return Path.Combine(GetRoot(), ".dsh-launcher-managed");
    }
}
