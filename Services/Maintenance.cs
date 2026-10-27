using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MondayNotch.Services;

/// <summary>Settings > Advanced actions, plus the name used by the startup greeting.</summary>
public static class Maintenance
{
    /// <summary>
    /// GitHub repository ("owner/name") whose latest release is compared with this build. Empty until the
    /// app is published somewhere; the update check then says so instead of pretending to be up to date.
    /// </summary>
    public const string UpdateRepository = "";

    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Human-readable result of looking for a newer release.</summary>
    public static async Task<string> CheckForUpdateAsync()
    {
        if (UpdateRepository.Length == 0)
            return "No update source is set up for this build.";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"MondayNotch/{CurrentVersion}");
            using JsonDocument json = JsonDocument.Parse(
                await http.GetStringAsync($"https://api.github.com/repos/{UpdateRepository}/releases/latest"));
            string tag = json.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
            return Version.TryParse(tag, out Version? latest) && latest > Version.Parse(CurrentVersion)
                ? $"Version {tag} is available."
                : "You're on the latest version.";
        }
        catch (Exception)
        {
            return "Couldn't check for updates. Are you online?";
        }
    }

    /// <summary>
    /// Deletes files in the user's temp folder that are more than a day old (fresh ones may belong to
    /// running installers). Files in use are skipped. Returns the number of bytes freed.
    /// </summary>
    public static Task<long> CleanTempFilesAsync() => Task.Run(() =>
    {
        long freed = 0;
        DateTime cutoff = DateTime.Now.AddDays(-1);
        var root = new DirectoryInfo(Path.GetTempPath());

        foreach (FileInfo file in SafeEnumerate(root))
        {
            try
            {
                if (file.LastWriteTime > cutoff)
                    continue;
                long size = file.Length;
                file.Delete();
                freed += size;
            }
            catch (Exception)
            {
                // In use or protected: leave it.
            }
        }

        // Remove folders the clean-up left empty (deepest first).
        foreach (DirectoryInfo directory in SafeDirectories(root))
        {
            try
            {
                if (directory.GetFileSystemInfos().Length == 0)
                    directory.Delete();
            }
            catch (Exception)
            {
            }
        }
        return freed;
    });

    private static IEnumerable<FileInfo> SafeEnumerate(DirectoryInfo root)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        return root.EnumerateFiles("*", options);
    }

    private static IEnumerable<DirectoryInfo> SafeDirectories(DirectoryInfo root)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var all = new List<DirectoryInfo>(root.EnumerateDirectories("*", options));
        all.Sort((a, b) => b.FullName.Length.CompareTo(a.FullName.Length));
        return all;
    }

    /// <summary>First name from the Windows account ("Hisyam Ramadhan" → "Hisyam"), falling back to the login name.</summary>
    public static string WindowsFirstName()
    {
        try
        {
            uint size = 256;
            var buffer = new StringBuilder((int)size);
            if (GetUserNameEx(NameDisplay, buffer, ref size) && buffer.Length > 0)
                return buffer.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        }
        catch (Exception)
        {
        }
        return Environment.UserName;
    }

    private const int NameDisplay = 3;

    [DllImport("secur32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetUserNameEx(int nameFormat, StringBuilder userName, ref uint size);
}
