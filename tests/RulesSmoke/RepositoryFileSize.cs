using System.Diagnostics;
using System.Text.Json;

internal static class RepositoryFileSize
{
    internal const int LineLimit = 400;
    internal const string ExceptionsFile = "scripts/file-size-exceptions.json";

    internal static IReadOnlyList<string> Inspect(string root)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, ExceptionsFile)));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Expected file-size exceptions keyed by repository-relative path.");

        var ceilings = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var exception in document.RootElement.EnumerateObject())
        {
            var entry = exception.Value;
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("maxLines", out var limit) || limit.ValueKind != JsonValueKind.Number ||
                !limit.TryGetDouble(out var maxLines) || maxLines <= LineLimit ||
                maxLines > int.MaxValue || !double.IsInteger(maxLines) ||
                !entry.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(reason.GetString()))
                throw new InvalidDataException($"{exception.Name} needs a maxLines integer above {LineLimit} and a reason.");
            ceilings[exception.Name] = (int)maxLines;
        }

        var files = Git(root, "ls-files", "-z", "--cached", "--others", "--exclude-standard");
        var failures = new List<string>();
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in files.Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct().Order(StringComparer.Ordinal))
        {
            var path = Path.Combine(root, name);
            // Ignore paths without regular file content, including symlinks and worktree deletions.
            if (!File.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                continue;

            var lines = CountTextLines(File.ReadAllBytes(path));
            if (lines <= LineLimit)
                continue;

            active.Add(name);
            var ceiling = ceilings.GetValueOrDefault(name, LineLimit);
            if (lines > ceiling)
                failures.Add($"{name}: {lines} lines exceed the limit of {ceiling}.");
        }

        foreach (var unused in ceilings.Keys.Except(active).Order(StringComparer.Ordinal))
            failures.Add($"{unused}: remove the unused file-size exception.");

        return failures;
    }

    private static int CountTextLines(ReadOnlySpan<byte> content)
    {
        // Match the Node suite's NUL-byte heuristic and count an unterminated final line.
        if (content.Contains((byte)0))
            return 0;
        return content.Count((byte)'\n') + (content.Length > 0 && content[^1] != '\n' ? 1 : 0);
    }

    internal static string Git(string directory, params string[] arguments)
    {
        var options = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true };
        foreach (var argument in arguments)
            options.ArgumentList.Add(argument);
        using var process = Process.Start(options) ?? throw new InvalidOperationException("Git did not start.");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git {arguments[0]} failed with exit code {process.ExitCode}.");
        return output;
    }
}
