using System.ComponentModel;
using System.Diagnostics;

namespace Ultima.Desktop;

internal static class DesktopBrowser
{
    internal static Process? Open(string url)
    {
        var executable = FindChromium();
        var data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (executable is not null && Path.IsPathFullyQualified(data))
        {
            // A dedicated profile keeps saves and gives the launcher ownership of this browser process.
            var settings = new ProcessStartInfo(executable);
            settings.ArgumentList.Add("--user-data-dir=" + Path.Combine(data, "UltimaDesktop", "Browser"));
            settings.ArgumentList.Add("--disable-background-mode");
            settings.ArgumentList.Add("--app=" + url);
            if (TryOpen(settings) is { } window) return window;
        }

        using var fallback = TryOpen(new ProcessStartInfo(url) { UseShellExecute = true });
        if (fallback is null)
            Console.Error.WriteLine("Abra o endereço acima em um navegador com WebGL.");
        return null;
    }

    private static Process? TryOpen(ProcessStartInfo settings)
    {
        try { return Process.Start(settings); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? FindChromium()
    {
        if (!OperatingSystem.IsWindows())
        {
            var directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries);
            string[] names = ["google-chrome", "google-chrome-stable", "chromium", "chromium-browser", "microsoft-edge"];
            return names.SelectMany(name => directories.Select(dir => Path.Combine(dir, name))).FirstOrDefault(File.Exists);
        }

        string[] folders =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ];
        string[] relative = [@"Microsoft\Edge\Application\msedge.exe", @"Google\Chrome\Application\chrome.exe",
            @"Chromium\Application\chrome.exe"];
        return relative.SelectMany(path => folders.Where(folder => folder.Length > 0)
            .Select(folder => Path.Combine(folder, path))).FirstOrDefault(File.Exists);
    }
}
