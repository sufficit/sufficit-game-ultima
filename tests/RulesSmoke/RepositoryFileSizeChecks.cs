using System.Text.Json;

internal static class RepositoryFileSizeChecks
{
    internal static void Run(Action<string, bool> check)
    {
        var root = RepositoryFileSize.Git(AppContext.BaseDirectory, "rev-parse", "--show-toplevel").TrimEnd('\r', '\n');
        var failures = RepositoryFileSize.Inspect(root);
        check(string.Join(Environment.NewLine, new[] { "Repository files respect their line limits" }.Concat(failures)),
            failures.Count == 0);

        var fixture = Directory.CreateTempSubdirectory("ultima-file-size-");
        try
        {
            _ = RepositoryFileSize.Git(fixture.FullName, "init", "--quiet");
            var source = Path.Combine(fixture.FullName, "new file.txt");
            var policy = Path.Combine(fixture.FullName, RepositoryFileSize.ExceptionsFile);
            Directory.CreateDirectory(Path.GetDirectoryName(policy)!);
            File.WriteAllText(policy, "{}");
            IReadOnlyList<string> Inspect() => RepositoryFileSize.Inspect(fixture.FullName);

            File.WriteAllText(source, string.Concat(Enumerable.Repeat("\r\n", 399)) + "last");
            check("400 lines pass with CRLF and an unterminated final line", Inspect().Count == 0);
            File.AppendAllText(source, "\r\nextra");
            const string violation = "new file.txt: 401 lines exceed the limit of 400.";
            check("A new file with 401 lines fails", Inspect().SequenceEqual([violation]));

            _ = RepositoryFileSize.Git(fixture.FullName, "add", "new file.txt");
            File.WriteAllText(Path.Combine(fixture.FullName, ".gitignore"), "*.txt\n");
            File.WriteAllText(Path.Combine(fixture.FullName, "ignored.txt"), new string('\n', 500));
            File.WriteAllText(Path.Combine(fixture.FullName, "binary"), "\0" + new string('\n', 500));
            check("Ignored and binary files are skipped while tracked files are checked",
                Inspect().SequenceEqual([violation]));

            File.WriteAllText(policy, JsonSerializer.Serialize(
                new Dictionary<string, object> { ["new file.txt"] = new { maxLines = 401, reason = "Fixture" } }));
            check("A justified exception allows its listed ceiling", Inspect().Count == 0);
            File.AppendAllText(source, "\nextra");
            check("An exception still fails above its listed ceiling",
                Inspect().SequenceEqual(["new file.txt: 402 lines exceed the limit of 401."]));
            File.WriteAllText(source, new string('\n', 400));
            const string stale = "new file.txt: remove the unused file-size exception.";
            check("A shrunken file requires removal of its exception", Inspect().SequenceEqual([stale]));
            File.Delete(source);
            check("A deleted file requires removal of its exception", Inspect().SequenceEqual([stale]));

            File.WriteAllText(policy, """{"new file.txt":{"maxLines":401,"reason":" "}}""");
            try
            {
                Inspect();
                check("An exception requires a reason", false);
            }
            catch (InvalidDataException error)
            {
                check("An exception requires a reason", error.Message.Contains("and a reason."));
            }
        }
        finally
        {
            fixture.Delete(recursive: true);
        }
    }
}
