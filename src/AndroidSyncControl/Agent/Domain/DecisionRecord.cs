namespace AndroidSyncControl.Agent.Domain;

/// <summary>
/// Bản ghi quyết định giải thích được (Explainable Decision Record) chuẩn SC-12.12.
/// <para>
/// Trả lời câu hỏi: "Tại sao Agent lại chọn cách này?"
/// </para>
/// </summary>
public sealed record DecisionRecord(
    string DecisionId,
    string TaskId,
    IReadOnlyList<string> Candidates,
    string SelectedCandidate,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> RejectedReasons,
    DateTimeOffset CreatedAt
);
