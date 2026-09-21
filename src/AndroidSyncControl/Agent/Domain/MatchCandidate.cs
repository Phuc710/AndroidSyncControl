namespace AndroidSyncControl.Agent.Domain;

/// <summary>
/// Một ứng viên trong danh sách kết quả của IPlaybookMatcher — trước khi Agent quyết định reuse hay không.
/// </summary>
public sealed record MatchCandidate(
    Playbook Playbook,

    /// <summary>Tổng confidence score (0.0 → 1.0) kết hợp từ tier đã match.</summary>
    float ConfidenceScore,

    /// <summary>Tier nào đã sinh ra candidate này.</summary>
    MatchMethod Method,

    /// <summary>Chuỗi bằng chứng debug — readable tại sao candidate này được chọn.</summary>
    IReadOnlyList<MatchEvidence> Evidences);
