using System.Text;

namespace ID6ModeTray;

internal static class DiagnosticLog
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private const int MaxFileCount = 3;
    private static readonly object Sync = new();
    private static readonly string PathName = Path.Combine(AppContext.BaseDirectory, "ID6ModeTray.log");
    private static string? lastMessage;
    private static int suppressedCount;

    public static void Write(string message)
    {
        lock (Sync)
        {
            try
            {
                if (message == lastMessage)
                {
                    suppressedCount++;
                    return;
                }

                if (suppressedCount > 0 && lastMessage is not null)
                {
                    WriteLine($"Previous message repeated {suppressedCount} times: {lastMessage}");
                }

                lastMessage = message;
                suppressedCount = 0;
                WriteLine(message);
            }
            catch
            {
                // Logging must never interrupt mode switching or terminate the tray.
            }
        }
    }

    private static void WriteLine(string message)
    {
        var line = $"{DateTime.Now:O} {message}{Environment.NewLine}";
        RotateIfNeeded(Encoding.UTF8.GetByteCount(line));
        File.AppendAllText(PathName, line, Encoding.UTF8);
    }

    private static void RotateIfNeeded(int incomingBytes)
    {
        if (!File.Exists(PathName) ||
            new FileInfo(PathName).Length + incomingBytes <= MaxFileBytes)
        {
            return;
        }

        for (var index = MaxFileCount - 1; index >= 1; index--)
        {
            var source = index == 1 ? PathName : $"{PathName}.{index - 1}";
            var destination = $"{PathName}.{index}";
            if (!File.Exists(source))
            {
                continue;
            }

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(source, destination);
        }
    }
}
