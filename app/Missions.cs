namespace Ultima.Missions;

public sealed record MissionDefinition(string Id, string Title, string Station, MissionObjective[] Objectives);
public sealed record MissionObjective(string Kind, string Target, int Required);
public sealed record MissionEvent(string Id, string Kind, string Target, string Agent);
public sealed record MissionProgress(string Kind, string Target, int Count, int Required);
public sealed record MissionView(string Id, string Title, string Station, MissionProgress[] Objectives, MissionEvent? LastEvent)
{
    public bool Complete => Objectives.All(objective => objective.Count >= objective.Required);
}
public sealed record MissionBoard(MissionView[] Missions);
