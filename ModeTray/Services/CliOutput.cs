using System.Runtime.InteropServices;
using System.Text;

namespace ID6ModeTray;

internal static class CliOutput
{
    private static TextWriter? writer;

    public static void WriteLine(string value)
    {
        if (writer is null)
        {
            try
            {
                AttachConsole(uint.MaxValue);
                Console.OutputEncoding = new UTF8Encoding(false);
                writer = TextWriter.Synchronized(new StreamWriter(
                    Console.OpenStandardOutput(),
                    new UTF8Encoding(false))
                {
                    AutoFlush = true
                });
            }
            catch
            {
                writer = TextWriter.Null;
            }
        }

        writer.WriteLine(value);
        writer.Flush();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);
}
