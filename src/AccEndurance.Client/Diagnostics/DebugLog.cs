using System.IO;

namespace AccEndurance.Client.Diagnostics;

public static class DebugLog
{
    private static readonly object Gate = new();
    private static readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "acc-client.log");

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
                System.IO.File.AppendAllText(Path, $"[{DateTime.Now:HH:mm:ss.fff}] {line}{Environment.NewLine}");
        }
        catch
        {
            // nothing else we can do
        }
    }
}