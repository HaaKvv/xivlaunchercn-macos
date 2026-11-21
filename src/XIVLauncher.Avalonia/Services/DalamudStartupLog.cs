using System.Globalization;

namespace XIVLauncher.Services;

internal static class DalamudStartupLog
{
    public static async Task<bool> IsReadyAsync(string path, DateTimeOffset attemptStarted)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                                     FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
            stream.Seek(Math.Max(0, stream.Length - 128 * 1024), SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync() is { } line)
            {
                var levelStart = line.IndexOf(" [", StringComparison.Ordinal);
                if (levelStart > 0 && line.Contains("Scene & ImGui setup OK!", StringComparison.Ordinal)
                                   && DateTimeOffset.TryParse(line.AsSpan(0, levelStart), CultureInfo.InvariantCulture,
                                                              DateTimeStyles.None, out var time)
                                   && time >= attemptStarted)
                    return true;
            }
        }
        catch (IOException)
        {
            // Dalamud can rotate or create its log during startup.
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }
}
