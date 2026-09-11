using System.Text.RegularExpressions;

namespace DshLauncher;

internal static class HarnessCompatibility
{
    internal static bool IsVersion(string value) => Regex.IsMatch(value,
        @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant);

    internal static bool IsVerified(PluginRecommendation item, string? version)
    {
        if (item.IsSkill) return true;
        if (string.IsNullOrWhiteSpace(version)) return false;
        var normalized = version.Trim().TrimStart('v').Split('+')[0];
        return IsVersion(normalized) && item.VerifiedHarnessVersions?.Contains(
            normalized, StringComparer.OrdinalIgnoreCase) == true;
    }
}
