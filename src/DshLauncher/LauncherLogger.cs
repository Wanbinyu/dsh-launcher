using System.Text;

namespace DshLauncher;

internal sealed class LauncherLogger : IDisposable
{
    private readonly object _sync = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    private LauncherLogger(string filePath, StreamWriter writer)
    {
        FilePath = filePath;
        _writer = writer;
    }

    public string FilePath { get; }

    public static LauncherLogger Create(string directory)
    {
        Directory.CreateDirectory(directory);
        var fileName = $"launcher-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log";
        var path = Path.Combine(directory, fileName);
        var writer = new StreamWriter(path, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
        return new LauncherLogger(path, writer);
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message) => Write("ERROR", message);

    public void Error(string message, Exception exception) => Write("ERROR", $"{message}: {exception}");

    public void WriteProcessOutput(string streamName, string line) => Write(streamName, line);

    private void Write(string level, string message)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _writer.WriteLine($"{DateTimeOffset.Now:O} [{level}] {message}");
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }
}
