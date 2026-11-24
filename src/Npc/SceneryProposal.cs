namespace Ultima.Npc;

public sealed record SceneryProposal(string Model, string Label, int Seed, double Detail)
{
    public static SceneryProposal? ForRequest(string text, int seed)
    {
        var request = text.ToLowerInvariant();
        string[] intentions = ["mudar", "mude", "trocar", "troque", "variar", "varie", "remodel", "novas", "novos"];
        if (!intentions.Any(request.Contains)) return null;
        if (request.Contains("árvor") || request.Contains("arvor") || request.Contains("pinheir"))
            return new("tree.pine", "Remodelar árvores", seed, 1.25);
        if (request.Contains("pedra") || request.Contains("rocha"))
            return new("rock.boulder", "Remodelar pedras", seed, 1);
        if (request.Contains("grama"))
            return new("scenery.grass", "Remodelar grama", seed, 1.4);
        return null;
    }
}
