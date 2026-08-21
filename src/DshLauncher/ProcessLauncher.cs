using System.Diagnostics;
using System.Text;

namespace DshLauncher;

internal sealed record RunnerSpec(
    string FilePath,
    IReadOnlyList<string> PrefixArguments,
    string WorkingDirectory,
    string Description,
    IReadOnlyDictionary<string, string?>? EnvironmentOverrides = null,
    string? DshVersion = null);

internal static class ProcessLauncher
{
    public static Process Start(
        RunnerSpec runner,
        bool redirectOutput,
        bool hiddenWindow)
    {
        var startInfo = CreateStartInfo(runner, redirectOutput, hiddenWindow);
        var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"Could not start {runner.Description}.");
        }

        return process;
    }

    public static async Task<ProcessResult> CaptureAsync(
        RunnerSpec runner,
        CancellationToken cancellationToken = default)
    {
        using var process = Start(runner, redirectOutput: true, hiddenWindow: true);
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, output, error);
    }

    public static ProcessStartInfo CreateStartInfo(
        RunnerSpec runner,
        bool redirectOutput,
        bool hiddenWindow)
    {
        var arguments = runner.PrefixArguments.ToArray();
        var isBatchFile = IsBatchFile(runner.FilePath);
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = runner.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = hiddenWindow,
            WindowStyle = hiddenWindow ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput,
            RedirectStandardInput = false
        };

        if (redirectOutput)
        {
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;
        }

        if (runner.EnvironmentOverrides is not null)
        {
            foreach (var (name, value) in runner.EnvironmentOverrides)
            {
                if (value is null)
                {
                    startInfo.Environment.Remove(name);
                }
                else
                {
                    startInfo.Environment[name] = value;
                }
            }
        }

        if (isBatchFile)
        {
            var commandLine = QuoteWindowsArgument(runner.FilePath);
            foreach (var argument in arguments)
            {
                commandLine += " " + QuoteCommandArgument(argument);
            }

            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            startInfo.Arguments = $"/d /s /c \"{commandLine}\"";
        }
        else
        {
            startInfo.FileName = runner.FilePath;
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        return startInfo;
    }

    public static bool IsBatchFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
    }

    public static string QuoteWindowsArgument(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            builder.Append('\\', backslashes);
            builder.Append(character);
            backslashes = 0;
        }

        builder.Append('\\', backslashes * 2);
        builder.Append('"');
        return builder.ToString();
    }

    private static string QuoteCommandArgument(string value)
    {
        if (value.Length > 0 && value.All(character => character is not (' ' or '\t' or '"' or '&' or '|' or '<' or '>' or '^' or '(' or ')')))
        {
            return value;
        }

        return QuoteWindowsArgument(value);
    }
}

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
