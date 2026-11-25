using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Ultima.Server;

/// <summary>
/// Ativos 3D do mundo (PLAN-PLAYER-ASSETS, fases 3 e 4): catálogo compartilhado de GLBs
/// com SHA-256, geração por prompt (gerador procedural local; adapter Meshy quando
/// MESHY_API_KEY está presente) e upload dos jogadores com validação server-side
/// (magic do GLB, tamanho e limite de triângulos). Persistência em ULTIMA_DATA_DIR com
/// gravação atômica (temporário + renome), como o mapa.
/// </summary>
sealed class WorldAssets(string dataDir, GameWorld game, ILogger log)
{
    const int MaxAssetBytes = 2 * 1024 * 1024;   // 2 MB por GLB
    const int MaxTris = 30_000;                  // low-poly por política
    const int MaxAssets = 200;
    const int MaxPrompt = 120;

    readonly object _lock = new();
    readonly HashSet<string>? _approvedUploads = ReadUploadAllowList();

    static HashSet<string>? ReadUploadAllowList()
    {
        var raw = Environment.GetEnvironmentVariable("ULTIMA_UPLOAD_SHA256_ALLOWLIST");
        if (raw is null) return null; // instalações existentes preservam a política anterior
        var values = raw.Split([',', ';', ' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        if (values.Any(value => value.Length != 64 || !value.All(Uri.IsHexDigit)))
            throw new InvalidOperationException("ULTIMA_UPLOAD_SHA256_ALLOWLIST contém um SHA-256 inválido.");
        return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
    }
    JsonObject? _catalog;
    DateTime _catalogStamp;
    readonly Dictionary<string, MeshyJob> _jobs = new();
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    string Dir => Path.Combine(dataDir, "assets");
    string CatalogPath => Path.Combine(Dir, "assets.json");

    // ---------- catálogo ----------

    JsonObject LoadCatalog()
    {
        var stamp = File.Exists(CatalogPath) ? File.GetLastWriteTimeUtc(CatalogPath) : DateTime.MinValue;
        if (_catalog is null || stamp != _catalogStamp)
        {
            try
            {
                var node = File.Exists(CatalogPath) ? JsonNode.Parse(File.ReadAllText(CatalogPath)) : null;
                _catalog = node as JsonObject ?? new JsonObject { ["version"] = 1, ["assets"] = new JsonArray() };
                if (_catalog["assets"] is not JsonArray) _catalog["assets"] = new JsonArray();
                _catalogStamp = stamp;
            }
            catch (Exception ex)
            {
                log.LogWarning("catálogo de assets ilegível: {Msg}", ex.Message);
                _catalog = new JsonObject { ["version"] = 1, ["assets"] = new JsonArray() };
                _catalogStamp = stamp;
            }
        }
        return _catalog;
    }

    void SaveCatalog(JsonObject catalog)
    {
        Directory.CreateDirectory(Dir);
        var tmp = CatalogPath + ".tmp";
        File.WriteAllText(tmp, catalog.ToJsonString(J.Opts));
        File.Move(tmp, CatalogPath, overwrite: true);
        _catalogStamp = File.GetLastWriteTimeUtc(CatalogPath);
    }

    static JsonArray AssetsOf(JsonObject catalog) => (JsonArray)catalog["assets"]!;

    static JsonObject Entry(string id, string file, string sha256, double scaleM, int tris, int bytes, string? prompt, string by)
        => new()
        {
            ["id"] = id, ["file"] = file, ["sha256"] = sha256, ["scaleM"] = Math.Round(scaleM, 3),
            ["tris"] = tris, ["bytes"] = bytes,
            ["prompt"] = prompt is null ? null : J.Cut(prompt, MaxPrompt),
            ["by"] = J.Cut(by, 40), ["at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

    public JsonObject Catalog()
    {
        lock (_lock) return new JsonObject { ["version"] = 1, ["assets"] = AssetsOf(LoadCatalog()).DeepClone() };
    }

    public string[] Ids()
    {
        lock (_lock) return AssetsOf(LoadCatalog()).OfType<JsonObject>().Select(asset => J.Str(asset["id"])).ToArray();
    }

    // Armazena bytes já validados sob id único endereçado por conteúdo (id-sha8.glb).
    // A entrada recebida é completada (arquivo/hora) e devolvida já consistente.
    JsonObject Store(byte[] bytes, string sha, JsonObject entry)
    {
        var fileName = entry["id"]!.GetValue<string>() + "-" + sha[..8] + ".glb";
        entry["file"] = "file/" + fileName;
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, fileName);
        File.WriteAllBytes(path + ".tmp", bytes);
        File.Move(path + ".tmp", path, overwrite: true);
        var catalog = (JsonObject)LoadCatalog().DeepClone();
        var assets = AssetsOf(catalog);
        // mesmo id já publicado: substitui a entrada (o arquivo antigo fica órfão, sem dano)
        for (var i = assets.Count - 1; i >= 0; i--)
        {
            if (assets[i]!["id"]!.GetValue<string>() == entry["id"]!.GetValue<string>()) assets.RemoveAt(i);
        }
        assets.Add(entry);
        SaveCatalog(catalog);
        _catalog = catalog;
        return entry;
    }

    public byte[]? ReadFile(string fileName)
    {
        if (!fileName.EndsWith(".glb", StringComparison.Ordinal) || fileName.Contains("..") || fileName.Contains('/'))
            return null;
        lock (_lock)
        {
            try { return File.ReadAllBytes(Path.Combine(Dir, fileName)); }
            catch (Exception) { return null; }
        }
    }

    public async Task PruneOrphansAsync(TimeSpan age, CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        try
        {
            do
            {
                lock (_lock) PruneOrphans(age);
            } while (await timer.WaitForNextTickAsync(stop));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private void PruneOrphans(TimeSpan age)
    {
        try
        {
            // Só um catálogo legível é autoridade suficiente para remover bytes.
            if (!File.Exists(CatalogPath) ||
                JsonNode.Parse(File.ReadAllText(CatalogPath)) is not JsonObject catalog ||
                catalog["assets"] is not JsonArray entries) return;
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry is not JsonObject meta) return;
                var file = J.Str(meta["file"]);
                if (!file.StartsWith("file/", StringComparison.Ordinal) || file.Length == 5) return;
                referenced.Add(file[5..]);
            }
            var olderThan = DateTime.UtcNow - age;
            foreach (var path in Directory.EnumerateFiles(Dir, "*.glb"))
            {
                var file = Path.GetFileName(path);
                if ((!file.StartsWith("player.", StringComparison.Ordinal) && !file.StartsWith("gen.", StringComparison.Ordinal)) ||
                    file.Length < 17 || file[^13] != '-' || !file[^12..^4].All(Uri.IsHexDigit) ||
                    referenced.Contains(file) || File.GetLastWriteTimeUtc(path) >= olderThan) continue;
                File.Delete(path);
                log.LogInformation("Arquivo GLB órfão expirado: {File}", file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            log.LogWarning(ex, "Coleta de arquivos órfãos adiada");
        }
    }

    // ---------- validação GLB ----------

    /// <summary>Confere magic/estrutura e devolve o total de triângulos (0 = inválido).</summary>
    static int InspectTris(byte[] bytes)
    {
        try
        {
            if (bytes.Length < 20) return 0;
            var b = bytes;
            if (BitConverter.ToUInt32(b, 0) != 0x46546C67) return 0;           // 'glTF'
            ReadOnlySpan<byte> json = default;
            var off = 12;
            while (off + 8 <= b.Length)
            {
                var len = BitConverter.ToInt32(b, off);
                var type = BitConverter.ToUInt32(b, off + 4);
                if (len < 0 || off + 8 + len > b.Length) return 0;
                if (type == 0x4E4F534A) json = b.AsSpan(off + 8, len);         // 'JSON'
                off += 8 + len;
            }
            if (json.IsEmpty) return 0;
            var doc = JsonNode.Parse(json.ToArray());
            var meshes = doc?["meshes"] as JsonArray;
            if (meshes is null || meshes.Count == 0) return 0;
            static int? CountOf(JsonNode? doc, JsonNode? index)
            {
                if (index is null || doc?["accessors"] is not JsonArray acc) return null;
                var i = (int)index;
                return i >= 0 && i < acc.Count && acc[i] is { } a ? (int?)a["count"] : null;
            }
            var tris = 0;
            foreach (var mesh in meshes)
            {
                foreach (var prim in (JsonArray?)mesh!["primitives"] ?? [])
                {
                    if (prim is null) return 0;
                    var t = CountOf(doc, prim["indices"]) / 3
                            ?? CountOf(doc, prim["attributes"]?["POSITION"]) / 3;
                    if (t is null) return 0;
                    tris += t.Value;
                }
            }
            return tris;
        }
        catch (Exception) { return 0; }
    }

    // ---------- upload (fase 4) ----------

    public (int, JsonObject) Upload(string? token, byte[] bytes, string? rawName, double scaleM)
    {
        if (!game.HasSession(token)) return (401, J.Err("invalid_token"));
        if (bytes.Length is < 200 or > MaxAssetBytes) return (413, J.Err("size_limit_2mb"));
        var tris = InspectTris(bytes);
        if (tris <= 0) return (415, J.Err("not_a_glb"));
        if (tris > MaxTris) return (422, J.Err("too_many_triangles"));
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (_approvedUploads is not null && !_approvedUploads.Contains(sha)) return (403, J.Err("asset_not_approved"));
        lock (_lock)
        {
            if (AssetsOf(LoadCatalog()).Count >= MaxAssets) return (507, J.Err("catalog_full"));
            var id = "player." + Slug(rawName, "modelo");
            var by = game.SessionName(token) ?? "jogador";
            var scale = Math.Clamp(scaleM <= 0 ? 1 : scaleM, 0.05, 12);
            JsonObject entry;
            try { entry = Store(bytes, sha, Entry(id, "", sha, scale, tris, bytes.Length, null, by)); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                log.LogError("armazenamento de assets indisponível (upload): {Msg}", ex.Message);
                return (507, J.Err("storage_unavailable"));
            }
            log.LogInformation("asset {Id} enviado por {By} ({Tris} tris)", id, by, tris);
            return (200, new JsonObject { ["asset"] = entry.DeepClone() });
        }
    }

    static string Slug(string? raw, string fallback)
    {
        var sb = new StringBuilder();
        foreach (var ch in (raw ?? "").ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is '-' or '_' or ' ') { if (sb.Length > 0 && sb[^1] != '-') sb.Append('-'); }
            if (sb.Length >= 40) break;
        }
        var s = sb.ToString().Trim('-');
        return s.Length >= 2 ? s : fallback;
    }

    // ---------- geração (fase 3) ----------

    public (int, JsonObject) Generate(string? token, string? prompt, string? seedText)
    {
        if (!game.HasSession(token)) return (401, J.Err("invalid_token"));
        var clean = J.Clean(prompt ?? "", MaxPrompt);
        if (clean.Length == 0) return (400, J.Err("prompt_required"));
        var by = game.SessionName(token) ?? "jogador";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(clean + "\u0001" + (seedText ?? "")));
        var seed = BitConverter.ToInt32(hash, 0);

        var meshyKey = Environment.GetEnvironmentVariable("MESHY_API_KEY");
        if (!string.IsNullOrWhiteSpace(meshyKey))
        {
            var job = new MeshyJob(Guid.NewGuid().ToString("N")[..12], clean, by);
            lock (_jobs) { _jobs[job.Id] = job; }
            _ = RunMeshyAsync(job, meshyKey, seed);
            return (202, new JsonObject { ["job"] = job.Id, ["engine"] = "meshy" });
        }

        // Gerador local (procedural, determinístico por prompt+seed): pipeline completo
        // sem depender de serviço externo — mesmo catálogo, mesmo hash, mesma verificação.
        var bytes = GlbProcedural.Build(clean, seed);
        var tris = InspectTris(bytes);
        lock (_lock)
        {
            if (AssetsOf(LoadCatalog()).Count >= MaxAssets) return (507, J.Err("catalog_full"));
            var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var id = "gen." + Slug(clean, "cristal") + "-" + sha[..6];
            JsonObject entry;
            try { entry = Store(bytes, sha, Entry(id, "", sha, GlbProcedural.ScaleM, tris, bytes.Length, clean, by)); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                log.LogError("armazenamento de assets indisponível (generate): {Msg}", ex.Message);
                return (507, J.Err("storage_unavailable"));
            }
            return (200, new JsonObject { ["engine"] = "local", ["asset"] = entry.DeepClone() });
        }
    }

    public (int, JsonObject) Job(string? token, string jobId)
    {
        if (!game.HasSession(token)) return (401, J.Err("invalid_token"));
        MeshyJob? job;
        lock (_jobs) { _jobs.TryGetValue(jobId, out job); }
        if (job is null) return (404, J.Err("job_not_found"));
        var o = new JsonObject { ["id"] = job.Id, ["status"] = job.Status, ["error"] = job.Error };
        if (job.Asset is { } a) o["asset"] = a.DeepClone();
        return (200, o);
    }

    sealed class MeshyJob(string id, string prompt, string by)
    {
        public string Id = id;
        public string Prompt = prompt;
        public string By = by;
        public volatile string Status = "running";
        public string? Error;
        public JsonObject? Asset;
    }

    /// <summary>Adapter Meshy (text-to-3D): cria a tarefa, aguarda conclusão e baixa o GLB.
    /// Ativado por MESHY_API_KEY; sem a chave a geração usa o motor local.</summary>
    async Task RunMeshyAsync(MeshyJob job, string key, int seed)
    {
        try
        {
            using var create = new HttpRequestMessage(HttpMethod.Post, "https://api.meshy.ai/openapi/v2/text-to-3d");
            create.Headers.Authorization = new("Bearer", key);
            create.Content = JsonContent.Create(new { mode = "preview", prompt = job.Prompt, art_style = "lowpoly" });
            using var created = await Http.SendAsync(create);
            var createdJson = JsonNode.Parse(await created.Content.ReadAsStringAsync());
            var taskId = createdJson?["result"]?.ToString();
            if (!created.IsSuccessStatusCode || string.IsNullOrEmpty(taskId))
            {
                job.Status = "error";
                job.Error = "meshy_create_failed";
                return;
            }
            for (var attempt = 0; attempt < 60 && job.Status == "running"; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                using var poll = new HttpRequestMessage(HttpMethod.Get, "https://api.meshy.ai/openapi/v2/text-to-3d/" + taskId);
                poll.Headers.Authorization = new("Bearer", key);
                using var polled = await Http.SendAsync(poll);
                var status = JsonNode.Parse(await polled.Content.ReadAsStringAsync())?["status"]?.ToString();
                if (status == "SUCCEEDED")
                {
                    var urls = JsonNode.Parse(await polled.Content.ReadAsStringAsync());
                    var url = urls?["pbr_model_url"]?.ToString() ?? urls?["model_url"]?.ToString();
                    if (string.IsNullOrEmpty(url)) { job.Status = "error"; job.Error = "meshy_no_model_url"; return; }
                    var bytes = await Http.GetByteArrayAsync(url);
                    var tris = InspectTris(bytes);
                    if (tris <= 0 || tris > MaxTris || bytes.Length > MaxAssetBytes) { job.Status = "error"; job.Error = "meshy_model_rejected"; return; }
                    lock (_lock)
                    {
                        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
                        var id = "gen." + Slug(job.Prompt, "modelo") + "-" + sha[..6];
                        job.Asset = Store(bytes, sha, Entry(id, "", sha, 1.0, tris, bytes.Length, job.Prompt, job.By));
                        job.Status = "done";
                    }
                    return;
                }
                if (status is "FAILED" or "EXPIRED") { job.Status = "error"; job.Error = "meshy_" + status?.ToLowerInvariant(); return; }
            }
            if (job.Status == "running") { job.Status = "error"; job.Error = "meshy_timeout"; }
        }
        catch (Exception ex)
        {
            job.Status = "error";
            job.Error = J.Cut(ex.Message, 80);
            log.LogWarning("job Meshy {Id} falhou: {Msg}", job.Id, ex.Message);
        }
    }
}

/// <summary>
/// Gerador procedural de GLB low-poly determinístico por prompt+seed (arquétipos: cristal,
/// totem e flor encantada — cores derivadas do hash). Escreve o binário glTF 2.0 na mão:
/// um mesh não indexado com flat normals e um material de cor sólida.
/// </summary>
static class GlbProcedural
{
    public const double ScaleM = 0.9;

    public static byte[] Build(string prompt, int seed)
    {
        var rng = new Rng((uint)seed);
        var hue = (float)((seed >>> 8) % 360) / 360f;
        var archetype = (seed >>> 3) % 3;

        var pos = new List<float>();
        var nrm = new List<float>();

        if (archetype == 0) BuildCrystal(pos, nrm, rng, hue);
        else if (archetype == 1) BuildTotem(pos, nrm, rng, hue);
        else BuildFlower(pos, nrm, rng, hue);

        var (r, g, b) = Hsl(hue, 0.55f, 0.55f);
        return Glb(pos.ToArray(), nrm.ToArray(), r, g, b);
    }

    static void Tri(List<float> pos, List<float> nrm, float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz)
    {
        var ux = bx - ax; var uy = by - ay; var uz = bz - az;
        var vx = cx - ax; var vy = cy - ay; var vz = cz - az;
        var nx = uy * vz - uz * vy; var ny = uz * vx - ux * vz; var nz = ux * vy - uy * vx;
        var len = MathF.Max(1e-6f, MathF.Sqrt(nx * nx + ny * ny + nz * nz));
        nx /= len; ny /= len; nz /= len;
        pos.Add(ax); pos.Add(ay); pos.Add(az); nrm.Add(nx); nrm.Add(ny); nrm.Add(nz);
        pos.Add(bx); pos.Add(by); pos.Add(bz); nrm.Add(nx); nrm.Add(ny); nrm.Add(nz);
        pos.Add(cx); pos.Add(cy); pos.Add(cz); nrm.Add(nx); nrm.Add(ny); nrm.Add(nz);
    }

    // Octaedro esticado (cristal): 8 faces em losango, anel no plano y=cy inclinado no eixo Z.
    static void Octa(List<float> pos, List<float> nrm, float cx, float cy, float cz, float radius, float up, float down, float tilt)
    {
        var rx = MathF.Cos(tilt) * radius;
        var rz = MathF.Sin(tilt) * radius;
        void Up(float ax, float az, float bx, float bz) =>
            Tri(pos, nrm, cx, cy + up, cz, cx + ax, cy, cz + az, cx + bx, cy, cz + bz);
        void Down(float ax, float az, float bx, float bz) =>
            Tri(pos, nrm, cx, cy - down, cz, cx + bx, cy, cz + bz, cx + ax, cy, cz + az);
        Up(rx, 0, 0, rz); Up(0, rz, -rx, 0); Up(-rx, 0, 0, -rz); Up(0, -rz, rx, 0);
        Down(rx, 0, 0, rz); Down(0, rz, -rx, 0); Down(-rx, 0, 0, -rz); Down(0, -rz, rx, 0);
    }

    static void Box(List<float> pos, List<float> nrm, float cx, float cy, float cz, float w, float h, float d, float yaw)
    {
        var c = MathF.Cos(yaw); var s = MathF.Sin(yaw);
        (float X, float Y, float Z) P(float x, float y, float z) => (cx + x * c - z * s, cy + y, cz + x * s + z * c);
        var h2 = h / 2f;
        var a = P(-w / 2, -h2, -d / 2); var b = P(w / 2, -h2, -d / 2); var cc = P(w / 2, -h2, d / 2); var dd = P(-w / 2, -h2, d / 2);
        var e = P(-w / 2, h2, -d / 2); var f = P(w / 2, h2, -d / 2); var g = P(w / 2, h2, d / 2); var h3 = P(-w / 2, h2, d / 2);
        Tri(pos, nrm, a.Item1, a.Item2, a.Item3, b.Item1, b.Item2, b.Item3, cc.Item1, cc.Item2, cc.Item3);
        Tri(pos, nrm, a.Item1, a.Item2, a.Item3, cc.Item1, cc.Item2, cc.Item3, dd.Item1, dd.Item2, dd.Item3);
        Tri(pos, nrm, e.Item1, e.Item2, e.Item3, g.Item1, g.Item2, g.Item3, f.Item1, f.Item2, f.Item3);
        Tri(pos, nrm, e.Item1, e.Item2, e.Item3, h3.Item1, h3.Item2, h3.Item3, g.Item1, g.Item2, g.Item3);
        Tri(pos, nrm, a.Item1, a.Item2, a.Item3, e.Item1, e.Item2, e.Item3, f.Item1, f.Item2, f.Item3);
        Tri(pos, nrm, a.Item1, a.Item2, a.Item3, f.Item1, f.Item2, f.Item3, b.Item1, b.Item2, b.Item3);
        Tri(pos, nrm, dd.Item1, dd.Item2, dd.Item3, g.Item1, g.Item2, g.Item3, h3.Item1, h3.Item2, h3.Item3);
        Tri(pos, nrm, dd.Item1, dd.Item2, dd.Item3, cc.Item1, cc.Item2, cc.Item3, g.Item1, g.Item2, g.Item3);
        Tri(pos, nrm, b.Item1, b.Item2, b.Item3, f.Item1, f.Item2, f.Item3, g.Item1, g.Item2, g.Item3);
        Tri(pos, nrm, b.Item1, b.Item2, b.Item3, g.Item1, g.Item2, g.Item3, cc.Item1, cc.Item2, cc.Item3);
        Tri(pos, nrm, a.Item1, a.Item2, a.Item3, h3.Item1, h3.Item2, h3.Item3, e.Item1, e.Item2, e.Item3);
        Tri(pos, nrm, a.Item1, a.Item2, a.Item3, dd.Item1, dd.Item2, dd.Item3, h3.Item1, h3.Item2, h3.Item3);
    }

    static void Cone(List<float> pos, List<float> nrm, float cx, float cy, float cz, float radius, float height, int sides, float yaw = 0)
    {
        for (var i = 0; i < sides; i++)
        {
            var a0 = yaw + i * MathF.PI * 2 / sides;
            var a1 = yaw + (i + 1) * MathF.PI * 2 / sides;
            Tri(pos, nrm,
                cx, cy + height, cz,
                cx + MathF.Cos(a0) * radius, cy, cz + MathF.Sin(a0) * radius,
                cx + MathF.Cos(a1) * radius, cy, cz + MathF.Sin(a1) * radius);
        }
    }

    static void BuildCrystal(List<float> pos, List<float> nrm, Rng rng, float hue)
    {
        // Rocha-base achatada + 1–3 cristais inclinados; tom pela matiz do prompt.
        Octa(pos, nrm, 0, 0.16f, 0, 0.42f, 0.10f, 0.16f, 0);
        var count = 1 + (int)(rng.Next() * 3);
        for (var i = 0; i < count; i++)
        {
            var ang = rng.Next() * MathF.PI * 2;
            var dist = 0.08f + rng.Next() * 0.12f;
            Octa(pos, nrm,
                MathF.Cos(ang) * dist, 0.18f + rng.Next() * 0.1f, MathF.Sin(ang) * dist,
                0.10f + rng.Next() * 0.08f, 0.5f + rng.Next() * 0.6f, 0.10f,
                (rng.Next() - 0.5f) * 0.7f);
        }
    }

    static void BuildTotem(List<float> pos, List<float> nrm, Rng rng, float hue)
    {
        var y = 0f;
        var layers = 3 + (int)(rng.Next() * 3);
        for (var i = 0; i < layers; i++)
        {
            var w = 0.42f - i * 0.06f;
            var h = 0.22f + rng.Next() * 0.16f;
            Box(pos, nrm, (rng.Next() - 0.5f) * 0.05f, y + h / 2, (rng.Next() - 0.5f) * 0.05f, w, h, w, rng.Next() * MathF.PI);
            y += h * 0.92f;
        }
        Cone(pos, nrm, 0, y, 0, 0.24f, 0.3f, 5, rng.Next() * MathF.PI);
    }

    static void BuildFlower(List<float> pos, List<float> nrm, Rng rng, float hue)
    {
        var stemH = 0.5f + rng.Next() * 0.4f;
        // caule hexagonal fino (cascata de cones invertidos dá o tubo)
        Cone(pos, nrm, 0, 0, 0, 0.05f, stemH, 6);
        var petals = 5 + (int)(rng.Next() * 3);
        for (var i = 0; i < petals; i++)
        {
            var ang = i * MathF.PI * 2 / petals + rng.Next() * 0.2f;
            var px = MathF.Cos(ang) * 0.16f;
            var pz = MathF.Sin(ang) * 0.16f;
            Octa(pos, nrm, px, stemH + 0.05f, pz, 0.14f, 0.06f, 0.12f, 0.9f + rng.Next() * 0.2f);
        }
        Octa(pos, nrm, 0, stemH + 0.06f, 0, 0.10f, 0.12f, 0.06f, 0);
    }

    static (float R, float G, float B) Hsl(float h, float s, float l)
    {
        var c = (1 - MathF.Abs(2 * l - 1)) * s;
        var x = c * (1 - MathF.Abs((h * 6 % 2) - 1));
        var m = l - c / 2;
        return (h * 6) switch
        {
            < 1 => (c + m, x + m, m),
            < 2 => (x + m, c + m, m),
            < 3 => (m, c + m, x + m),
            < 4 => (m, x + m, c + m),
            < 5 => (x + m, m, c + m),
            _ => (c + m, m, x + m),
        };
    }

    // Empacota glTF 2.0 binário: header + chunk JSON + chunk BIN (mesh não indexado).
    static byte[] Glb(float[] positions, float[] normals, float r, float g, float b)
    {
        var posBytes = new byte[positions.Length * 4];
        var nrmBytes = new byte[normals.Length * 4];
        for (var i = 0; i < positions.Length; i++) Buffer.BlockCopy(positions, i * 4, posBytes, i * 4, 4);
        for (var i = 0; i < normals.Length; i++) Buffer.BlockCopy(normals, i * 4, nrmBytes, i * 4, 4);

        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        for (var i = 0; i < positions.Length; i += 3)
        {
            minX = MathF.Min(minX, positions[i]); maxX = MathF.Max(maxX, positions[i]);
            minY = MathF.Min(minY, positions[i + 1]); maxY = MathF.Max(maxY, positions[i + 1]);
            minZ = MathF.Min(minZ, positions[i + 2]); maxZ = MathF.Max(maxZ, positions[i + 2]);
        }
        var vertexCount = positions.Length / 3;
        var doc = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "UltimaServer procedural" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray { new JsonObject { ["nodes"] = new JsonArray { 0 } } },
            ["nodes"] = new JsonArray { new JsonObject { ["mesh"] = 0, ["name"] = "generated" } },
            ["meshes"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "gen",
                    ["primitives"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["attributes"] = new JsonObject { ["POSITION"] = 0, ["NORMAL"] = 1 },
                            ["material"] = 0
                        }
                    }
                }
            },
            ["materials"] = new JsonArray
            {
                new JsonObject
                {
                    ["pbrMetallicRoughness"] = new JsonObject
                    {
                        ["baseColorFactor"] = new JsonArray { Math.Round(r, 4), Math.Round(g, 4), Math.Round(b, 4), 1 },
                        ["metallicFactor"] = 0, ["roughnessFactor"] = 0.85
                    },
                    ["doubleSided"] = true
                }
            },
            ["accessors"] = new JsonArray
            {
                new JsonObject
                {
                    ["bufferView"] = 0, ["componentType"] = 5126, ["count"] = vertexCount, ["type"] = "VEC3",
                    ["min"] = new JsonArray { minX, minY, minZ }, ["max"] = new JsonArray { maxX, maxY, maxZ }
                },
                new JsonObject { ["bufferView"] = 1, ["componentType"] = 5126, ["count"] = vertexCount, ["type"] = "VEC3" }
            },
            ["bufferViews"] = new JsonArray
            {
                new JsonObject { ["buffer"] = 0, ["byteOffset"] = 0, ["byteLength"] = posBytes.Length, ["target"] = 34962 },
                new JsonObject { ["buffer"] = 0, ["byteOffset"] = posBytes.Length, ["byteLength"] = nrmBytes.Length, ["target"] = 34962 }
            },
            ["buffers"] = new JsonArray { new JsonObject { ["byteLength"] = posBytes.Length + nrmBytes.Length } }
        };

        var pad = 4 - (posBytes.Length + nrmBytes.Length) % 4;
        if (pad == 4) pad = 0;
        var binLength = posBytes.Length + nrmBytes.Length + pad;
        var json = Encoding.UTF8.GetBytes(doc.ToJsonString(J.Opts));
        var jsonPad = 4 - json.Length % 4; if (jsonPad == 4) jsonPad = 0;
        var jsonChunk = json.Length + jsonPad;
        var binStart = 12 + 8 + jsonChunk;
        var total = binStart + 8 + binLength;

        var glb = new byte[total];
        BinaryPrimitivesWriteUInt32(glb, 0, 0x46546C67);
        BinaryPrimitivesWriteUInt32(glb, 4, 2);
        BinaryPrimitivesWriteUInt32(glb, 8, (uint)total);
        BinaryPrimitivesWriteUInt32(glb, 12, (uint)jsonChunk);
        BinaryPrimitivesWriteUInt32(glb, 16, 0x4E4F534A);
        Array.Copy(json, 0, glb, 20, json.Length);
        for (var i = 0; i < jsonPad; i++) glb[20 + json.Length + i] = (byte)' ';
        BinaryPrimitivesWriteUInt32(glb, binStart, (uint)binLength);
        BinaryPrimitivesWriteUInt32(glb, binStart + 4, 0x004E4942);
        Array.Copy(posBytes, 0, glb, binStart + 8, posBytes.Length);
        Array.Copy(nrmBytes, 0, glb, binStart + 8 + posBytes.Length, nrmBytes.Length);
        return glb;
    }

    static void BinaryPrimitivesWriteUInt32(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)value;
        buf[offset + 1] = (byte)(value >> 8);
        buf[offset + 2] = (byte)(value >> 16);
        buf[offset + 3] = (byte)(value >> 24);
    }

    /// <summary>PRNG determinístico mínimo (xorshift) — mesmo prompt+seed, mesmo modelo.</summary>
    struct Rng(uint seed)
    {
        uint _s = seed == 0 ? 0x9E3779B9 : seed;
        public float Next()
        {
            _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
            return (_s & 0xFFFFFF) / 16777215f;
        }
    }
}
