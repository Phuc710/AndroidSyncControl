using AndroidSyncControl.Agent.Domain;

namespace AndroidSyncControl.Agent.Abstractions;

/// <summary>
/// Persist và truy vấn Playbook + Experience.
/// Impl: <c>JsonPlaybookRepository</c> — lưu tại <c>agent-data/</c>.
/// </summary>
public interface IPlaybookRepository
{
    // ── Playbook ──────────────────────────────────────────────────────────────
    Task<Playbook?> GetAsync(string id, int? version = null, CancellationToken ct = default);
    Task<IReadOnlyList<Playbook>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Playbook>> GetByStateAsync(PlaybookState state, CancellationToken ct = default);

    /// <summary>
    /// Lưu hoặc cập nhật Playbook. Version cũ KHÔNG bị overwrite (SC-11).
    /// Nếu version đã tồn tại → throw InvalidOperationException.
    /// </summary>
    Task SaveAsync(Playbook playbook, CancellationToken ct = default);

    // ── Experience ────────────────────────────────────────────────────────────
    /// <summary>Experience không bao giờ bị xóa — chỉ có AppendAsync, không có DeleteAsync.</summary>
    Task AppendExperienceAsync(Experience experience, CancellationToken ct = default);
    Task<IReadOnlyList<Experience>> GetExperiencesAsync(string? playbookId = null, CancellationToken ct = default);
}
