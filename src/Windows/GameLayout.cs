using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ultima.Windows;

public sealed record GameLayout(int Version, string Title, GameControl[] Controls)
{
    public static GameLayout Conversation { get; } = Load("conversation");
    public static GameLayout Journal { get; } = Load("journal");

    private static GameLayout Load(string name)
    {
        using var stream = typeof(GameLayout).Assembly.GetManifestResourceStream($"Ultima.Windows.Layouts.{name}.json")
            ?? throw new InvalidDataException($"Layout ausente: {name}");
        var layout = JsonSerializer.Deserialize(stream, GameLayoutJson.Default.GameLayout);
        return layout is { Version: 1 } ? layout : throw new InvalidDataException($"Layout incompatível: {name}");
    }
}

public sealed record GameControl(
    string Type, string? Text = null, string? Bind = null, string? Action = null,
    string? When = null, string? Style = null, string? Icon = null, string? Id = null,
    string? Label = null, int? MaxLength = null, GameControl[]? Children = null, string? DisabledWhen = null);

public sealed class WindowState
{
    public Ultima.Npc.DialogView? Conversation { get; set; }
    public IReadOnlyList<Sufficit.AI.Genius.Embedded.ConversationTurn> ArchivedTurns { get; set; } = [];

    public Dictionary<string, string> Text { get; } = [];
    public Dictionary<string, IReadOnlyList<string>> Lists { get; } = [];

    public string Read(string? key) => key is not null && Text.TryGetValue(key, out var value) ? value : "";
    public string Content(GameControl control) => control.Bind is null ? control.Text ?? "" : Read(control.Bind);
    public bool Visible(GameControl control) => control.When is null || Read(control.When).Length != 0;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GameLayout))]
internal sealed partial class GameLayoutJson : JsonSerializerContext;
