using System.Text.Json.Nodes;

namespace Ultima.Server;

sealed partial class GameWorld
{
    internal const string CharacterBodyRoute = "/v1/game/characters/me/body";
    internal const string CharacterProfileHeader = "X-Character-Profile-Key";

    internal (int, JsonObject) ReadCharacterBody(string? token, string? profileKey)
    {
        lock (_lock)
        {
            var key = Session(token)?.ProfileKey ?? profileKey;
            return CharacterProfiles.IsKey(key)
                ? (200, CharacterProfiles.Reply(_characterProfiles.Read(key!)))
                : (401, J.Err("invalid_token"));
        }
    }

    internal (int, JsonObject) UpdateCharacterBody(string? token, JsonObject patch)
    {
        lock (_lock)
        {
            var character = Session(token);
            if (character is null) return (401, J.Err("invalid_token"));
            if (!CmdAllowed(character, J.Now)) return (429, J.Err("rate_limited"));
            if (patch.Count == 0) return (400, J.Err("invalid_body_profile"));
            var result = _characterProfiles.Update(character.ProfileKey, patch);
            if (result.Item1 == 200) character.Last = J.Now;
            return result;
        }
    }
}
