using System.Text.Json;
using System.Text.Json.Serialization;
using AndroidSyncControl.Agent.Abstractions;
using AndroidSyncControl.Agent.Domain;

namespace AndroidSyncControl.Agent.Repository;

/// <summary>
/// Persist Playbook và Experience dưới dạng JSON tại <c>&lt;project-root&gt;/agent-data/</c>.
/// <para>
/// Layout:
/// <br/><c>agent-data/playbooks/{id}_v{version}.json</c>
/// <br/><c>agent-data/experiences/{id}.json</c>
/// </para>
/// SC-11: Experience là append-only — không có DeleteAsync, không có overwrite.
/// SC-11: Playbook version đã lưu không được overwrite — SaveAsync ném exception nếu trùng version.
/// </summary>
public sealed class JsonPlaybookRepository : IPlaybookRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented    = true,
        Converters       = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _playbookDir;
    private readonly string _experienceDir;

    public JsonPlaybookRepository(string agentDataRoot)
    {
        _playbookDir   = Path.Combine(agentDataRoot, "playbooks");
        _experienceDir = Path.Combine(agentDataRoot, "experiences");
        Directory.CreateDirectory(_playbookDir);
        Directory.CreateDirectory(_experienceDir);
    }

    // ── Factory: resolve agent-data next to project root ─────────────────────

    /// <summary>
    /// Resolves <c>agent-data/</c> directory walking up from application base directory
    /// until a <c>tools/android/</c> sibling is found (project root indicator).
    /// </summary>
    public static JsonPlaybookRepository CreateDefault()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        var dir = new DirectoryInfo(baseDir);

        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tools", "android")))
            {
                string devAgentData = Path.Combine(dir.FullName, "agent-data");
                if (Directory.Exists(devAgentData))
                    return new JsonPlaybookRepository(devAgentData);
            }
            dir = dir.Parent;
        }

        // Production Invariant: %LOCALAPPDATA%\AndroidSyncControl\agent-data
        return new JsonPlaybookRepository(Infrastructure.AppPaths.AgentDataDir);
    }

    // ── IPlaybookRepository — Playbook ────────────────────────────────────────

    public async Task<Playbook?> GetAsync(string id, int? version = null, CancellationToken ct = default)
    {
        if (version.HasValue)
        {
            string path = PlaybookPath(id, version.Value);
            return File.Exists(path) ? await ReadJsonAsync<Playbook>(path, ct) : null;
        }

        // Latest version = highest version number for this id
        return (await GetAllByIdAsync(id, ct))
            .OrderByDescending(p => p.Version)
            .FirstOrDefault();
    }

    public async Task<IReadOnlyList<Playbook>> GetAllAsync(CancellationToken ct = default)
    {
        var files = Directory.GetFiles(_playbookDir, "*.json");
        var result = new List<Playbook>();
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            var pb = await ReadJsonAsync<Playbook>(f, ct);
            if (pb is not null) result.Add(pb);
        }
        return result;
    }

    public async Task<IReadOnlyList<Playbook>> GetByStateAsync(PlaybookState state, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.Where(p => p.State == state).ToList();
    }

    public async Task SaveAsync(Playbook playbook, CancellationToken ct = default)
    {
        string path = PlaybookPath(playbook.Id, playbook.Version);
        if (File.Exists(path))
            throw new InvalidOperationException(
                $"Playbook '{playbook.Id}' v{playbook.Version} already exists. " +
                "Use BumpVersion() to create a new version. (SC-11: no overwrite)");

        await WriteJsonAsync(path, playbook, ct);
    }

    // ── IPlaybookRepository — Experience ──────────────────────────────────────

    public async Task AppendExperienceAsync(Experience experience, CancellationToken ct = default)
    {
        string path = ExperiencePath(experience.Id);
        if (File.Exists(path))
            throw new InvalidOperationException(
                $"Experience '{experience.Id}' already exists — IDs must be unique.");
        await WriteJsonAsync(path, experience, ct);
    }

    public async Task<IReadOnlyList<Experience>> GetExperiencesAsync(
        string? playbookId = null,
        CancellationToken ct = default)
    {
        var files = Directory.GetFiles(_experienceDir, "*.json");
        var result = new List<Experience>();
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            var exp = await ReadJsonAsync<Experience>(f, ct);
            if (exp is null) continue;
            if (playbookId is null || exp.PlaybookUsed == playbookId)
                result.Add(exp);
        }
        return result.OrderBy(e => e.RecordedAt).ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<Playbook>> GetAllByIdAsync(string id, CancellationToken ct)
    {
        var files = Directory.GetFiles(_playbookDir, $"{id}_v*.json");
        var result = new List<Playbook>();
        foreach (var f in files)
        {
            var pb = await ReadJsonAsync<Playbook>(f, ct);
            if (pb is not null) result.Add(pb);
        }
        return result;
    }

    private string PlaybookPath(string id, int version) =>
        Path.Combine(_playbookDir, $"{id}_v{version}.json");

    private string ExperiencePath(string id) =>
        Path.Combine(_experienceDir, $"{id}.json");

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(fs, JsonOpts, ct);
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken ct)
    {
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, value, JsonOpts, ct);
    }
}
