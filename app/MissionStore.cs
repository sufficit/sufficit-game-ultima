using System.Text.Json;
using Ultima.Missions;

namespace Ultima.Server;

internal sealed class MissionStore(string path)
{
    internal static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private MissionRun[]? _runs;

    public MissionBoard Read(string environment)
    {
        lock (_gate) return new(Runs.Where(run => run.Environment == environment).Select(View).ToArray());
    }

    public (int Status, string Error, MissionView? Value) Create(string environment, MissionDefinition? definition)
    {
        if (definition is null || !IsId(definition.Id) || !IsText(definition.Title, 120) ||
            !IsId(definition.Station) ||
            definition.Objectives is not { Length: >= 1 and <= 8 } ||
            definition.Objectives.Any(objective => objective is null || !IsKind(objective.Kind) ||
                !IsId(objective.Target) || objective.Required is < 1 or > 100) ||
            definition.Objectives.Select(objective => (objective.Kind, objective.Target)).Distinct().Count() != definition.Objectives.Length)
            return (400, "invalid_mission", null);

        lock (_gate)
        {
            if (Runs.Any(run => run.Environment == environment && run.Definition.Id == definition.Id)) return (409, "mission_exists", null);
            if (Runs.Length >= 100) return (409, "mission_limit", null);
            var run = new MissionRun(environment, definition, []);
            Save([.. Runs, run]);
            return (201, "", View(run));
        }
    }

    public (int Status, string Error, MissionView? Value) Record(string environment, string id, MissionEvent? action)
    {
        if (!IsId(id) || action is null || !IsId(action.Id) || !IsKind(action.Kind) ||
            !IsId(action.Target) || !IsText(action.Agent, 80))
            return (400, "invalid_event", null);

        lock (_gate)
        {
            var index = Array.FindIndex(Runs, run => run.Environment == environment && run.Definition.Id == id);
            if (index < 0) return (404, "mission_not_found", null);
            var run = Runs[index];
            var recorded = Array.Find(run.Events, item => item.Id == action.Id);
            if (recorded is not null)
                return recorded == action ? (200, "", View(run)) : (409, "event_id_conflict", null);

            var objective = Array.Find(run.Definition.Objectives, item => Matches(item, action));
            if (objective is null) return (422, "event_not_applicable", null);
            if (run.Events.Count(item => Matches(objective, item)) >= objective.Required)
                return (409, "objective_complete", null);

            var changed = run with { Events = [.. run.Events, action] };
            var next = (MissionRun[])Runs.Clone();
            next[index] = changed;
            Save(next);
            return (201, "", View(changed));
        }
    }

    private MissionRun[] Runs => _runs ??= File.Exists(path)
        ? JsonSerializer.Deserialize<MissionRun[]>(File.ReadAllBytes(path), Json)
            ?? throw new InvalidDataException("The mission snapshot is empty.")
        : [];

    private void Save(MissionRun[] next)
    {
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, next, Json);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
        _runs = next;
    }

    private static MissionView View(MissionRun run) => new(
        run.Definition.Id, run.Definition.Title, run.Definition.Station,
        run.Definition.Objectives.Select(objective => new MissionProgress(objective.Kind, objective.Target,
            run.Events.Count(action => Matches(objective, action)), objective.Required)).ToArray(),
        run.Events.LastOrDefault());

    private static bool Matches(MissionObjective objective, MissionEvent action) =>
        objective.Kind == action.Kind && objective.Target == action.Target;

    private static bool IsKind(string? kind) => kind is "task.started" or "task.completed" or "task.reviewed";
    internal static bool IsId(string? value) => value is { Length: >= 1 and <= 64 } &&
        value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-' or '_');
    private static bool IsText(string? value, int max) => value is not null && value.Length <= max &&
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);

    private sealed record MissionRun(string Environment, MissionDefinition Definition, MissionEvent[] Events);
}
