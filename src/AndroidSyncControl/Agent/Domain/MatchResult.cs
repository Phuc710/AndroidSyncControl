namespace AndroidSyncControl.Agent.Domain;

/// <summary>
/// Kết quả cuối cùng của <c>IPlaybookMatcher.FindMatchingAsync</c>.
/// AgentOrchestrator dùng <see cref="ShouldReuse"/> để quyết định Reuse hay Explore.
/// </summary>
public sealed record MatchResult(
    /// <summary>Playbook tốt nhất — null nếu Method == None.</summary>
    Playbook? BestMatch,

    /// <summary>Confidence score của BestMatch (0.0 → 1.0).</summary>
    float ConfidenceScore,

    /// <summary>Tier đã sinh ra BestMatch.</summary>
    MatchMethod Method,

    /// <summary>
    /// True nếu tất cả preconditions của BestMatch đã được thỏa mãn tại thời điểm match.
    /// </summary>
    bool PreconditionsPassed,

    /// <summary>Toàn bộ danh sách ứng viên — phục vụ debug và fallback logic.</summary>
    IReadOnlyList<MatchCandidate> Candidates)
{
    private const float ConfidenceThreshold = 0.75f;

    /// <summary>
    /// True khi Agent nên reuse BestMatch thay vì Explore.
    /// Điều kiện: confidence đủ cao + preconditions pass + state là Verified/Published.
    /// </summary>
    public bool ShouldReuse =>
        BestMatch is not null &&
        ConfidenceScore >= ConfidenceThreshold &&
        PreconditionsPassed &&
        Method != MatchMethod.None &&
        BestMatch.State is PlaybookState.Verified or PlaybookState.Published;

    /// <summary>Singleton kết quả "không tìm thấy gì" — tránh null propagation.</summary>
    public static readonly MatchResult NoMatch = new(
        BestMatch: null,
        ConfidenceScore: 0f,
        Method: MatchMethod.None,
        PreconditionsPassed: false,
        Candidates: []);
}
