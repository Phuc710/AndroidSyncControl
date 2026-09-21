namespace AndroidSyncControl.Agent.Domain;

/// <summary>
/// Các nhánh thực thi mà Agent đã đi qua trong vòng đời tác vụ.
/// </summary>
public enum ExecutionPath
{
    /// <summary>Tái sử dụng Playbook đã được Verified & Published trước đó.</summary>
    ReusedVerifiedPlaybook,

    /// <summary>Chưa có sách hoặc sách cũ lỗi -> Tự khám phá, phản tư, tối ưu, test và ban hành sách mới.</summary>
    LearnedAndPublished,

    /// <summary>Sách đã được sinh nhưng chưa đủ điểm verified hoặc ở chế độ candidate.</summary>
    CandidateSaved,

    /// <summary>Phát hiện thoái lui chất lượng (Regression) hoặc thất bại toàn diện.</summary>
    Failed
}

/// <summary>
/// Kết quả chi tiết sau khi Agent hoàn thành vòng đời thực thi tác vụ.
/// Cung cấp đầy đủ thông tin để UI hiển thị trực quan và minh bạch (Audit Trail).
/// </summary>
public sealed record AgentExecutionResult
{
    public bool Success { get; init; }

    public ExecutionPath Path { get; init; }

    public string? PlaybookId { get; init; }

    public int? PlaybookVersion { get; init; }

    public string? FailureReason { get; init; }

    public string? SelectedStrategy { get; init; }

    public float Confidence { get; init; }

    public IReadOnlyList<DecisionRecord> Decisions { get; init; } = [];

    public string? ExperienceId { get; init; }

    public string? EvaluationId { get; init; }

    public EvaluationReport? Evaluation { get; init; }
}
