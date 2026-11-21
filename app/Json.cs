using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ultima.Server;

/// <summary>Utilitários de JSON, tempo e saneamento usados por toda a API.</summary>
static partial class J
{
    public static readonly JsonSerializerOptions Opts = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    /// <summary>Tempo Unix em segundos (mesma escala do backend anterior).</summary>
    public static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    public static double R(double v, int digits) => Math.Round(v, digits, MidpointRounding.ToEven);

    public static string Cut(string s, int max) => s.Length > max ? s[..max] : s;

    public static string Hex(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    public static JsonObject Err(string code) => new() { ["error"] = code };

    public static JsonArray Arr(IEnumerable<JsonNode?> items) => new(items.ToArray());

    public static JsonArray StrArr(IEnumerable<string> items) => new(items.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());

    [GeneratedRegex("[\\x00-\\x1f\\x7f<>]")]
    private static partial Regex CtrlRegex();

    /// <summary>Texto "falso" (null, false, 0, vazio) vira "", como str(x or "").</summary>
    public static string Str(JsonNode? n)
    {
        if (n is null) return "";
        if (n is JsonValue v)
        {
            switch (v.GetValueKind())
            {
                case JsonValueKind.String: return v.GetValue<string>();
                case JsonValueKind.True: return "True";
                case JsonValueKind.False: return "";
                case JsonValueKind.Null: return "";
                case JsonValueKind.Number: return v.GetValue<double>() == 0 ? "" : n.ToJsonString();
            }
        }
        if (n is JsonObject o && o.Count == 0) return "";
        if (n is JsonArray a && a.Count == 0) return "";
        return n.ToJsonString(Opts);
    }

    /// <summary>Remove controles e &lt; &gt;, apara e limita o tamanho.</summary>
    public static string Clean(JsonNode? n, int limit) => Cut(CtrlRegex().Replace(Str(n), " ").Trim(), limit);

    public static string Clean(string s, int limit) => Cut(CtrlRegex().Replace(s, " ").Trim(), limit);

    /// <summary>Somente string não vazia (token de sessão).</summary>
    public static string? StrOrNull(JsonNode? n) =>
        n is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() is { Length: > 0 } s ? s : null;

    /// <summary>Número como float(x): aceita número, texto numérico e booleano; recusa não finitos.</summary>
    public static bool TryFloat(JsonNode? n, out double d)
    {
        d = 0;
        if (n is not JsonValue v) return false;
        switch (v.GetValueKind())
        {
            case JsonValueKind.Number: d = v.GetValue<double>(); break;
            case JsonValueKind.String:
                if (!double.TryParse(v.GetValue<string>().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    return false;
                break;
            case JsonValueKind.True: d = 1; break;
            case JsonValueKind.False: d = 0; break;
            default: return false;
        }
        return double.IsFinite(d);
    }

    /// <summary>Inteiro como int(x): número é truncado, texto precisa ser inteiro.</summary>
    public static bool TryInt(JsonNode? n, out long i)
    {
        i = 0;
        if (n is not JsonValue v) return false;
        switch (v.GetValueKind())
        {
            case JsonValueKind.Number:
                var d = v.GetValue<double>();
                if (!double.IsFinite(d) || Math.Abs(d) > 1e12) return false;
                i = (long)Math.Truncate(d);
                return true;
            case JsonValueKind.String:
                return long.TryParse(v.GetValue<string>().Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out i)
                       && Math.Abs(i) <= 1_000_000_000_000L;
            case JsonValueKind.True: i = 1; return true;
            case JsonValueKind.False: i = 0; return true;
            default: return false;
        }
    }

    /// <summary>Inteiro estrito para edição do mapa: 0 = ok, 1 = ausente/inválido, 2 = não inteiro.</summary>
    public static int StrictInt(JsonNode? n, out long i)
    {
        i = 0;
        if (n is not JsonValue v) return 1;
        switch (v.GetValueKind())
        {
            case JsonValueKind.Number:
                var d = v.GetValue<double>();
                if (!double.IsFinite(d) || Math.Abs(d) > 1e12) return 1;
                var t = Math.Truncate(d);
                i = (long)t;
                return t == d ? 0 : 2;
            case JsonValueKind.String:
                return long.TryParse(v.GetValue<string>().Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) ? 2 : 1;
            default: return 1;
        }
    }

    public static bool IsBool(JsonNode? n) =>
        n is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False;
}

/// <summary>Lê app/content.json a cada uso (permite editar o conteúdo sem recompilar).</summary>
sealed class ContentStore(string path)
{
    public JsonObject? Load()
    {
        try { return JsonNode.Parse(File.ReadAllBytes(path)) as JsonObject; }
        catch (Exception) { return null; }
    }

    public JsonObject Guardia() =>
        (Load()?["npcs"] as JsonObject)?["guardia"] is JsonObject g ? (JsonObject)g.DeepClone() : new JsonObject();
}
