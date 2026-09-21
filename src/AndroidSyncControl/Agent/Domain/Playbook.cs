namespace AndroidSyncControl.Agent.Domain;

/// <summary>
/// "Sách hướng dẫn" — không chỉ là một danh sách action steps.
/// Chứa đủ context để Agent tự reuse, tự debug, và tự improve khi thất bại.
/// </summary>
public sealed record Playbook
{
    public required string Id { get; init; }
    public required int Version { get; init; }
    public required PlaybookState State { get; init; }

    // ── Task descriptor ──────────────────────────────────────────────────────
    /// <summary>Intent ngắn gọn — ví dụ: "configure chrome account".</summary>
    public required string Intent { get; init; }

    /// <summary>Domain tags — dùng cho T2 Jaccard matching.</summary>
    public required string[] Contexts { get; init; }

    // ── Preconditions ─────────────────────────────────────────────────────────
    /// <summary>
    /// Điều kiện phải thỏa trước khi chạy — ví dụ: "device_connected", "chrome_installed".
    /// </summary>
    public required string[] Preconditions { get; init; }

    // ── Strategy ──────────────────────────────────────────────────────────────
    /// <summary>
    /// Chuỗi bước ở mức abstract — ví dụ: "launch_app", "wait_for_ui", "find_element".
    /// KHÔNG dùng hardcoded coordinates.
    /// </summary>
    public required string[] Strategy { get; init; }

    /// <summary>Rules cố định áp dụng khi chạy — ví dụ: "Do not use hardcoded coordinates".</summary>
    public required string[] Rules { get; init; }

    /// <summary>Fallback steps khi strategy gặp lỗi — ví dụ: "refresh_ui", "relaunch_app".</summary>
    public required string[] Fallbacks { get; init; }

    // ── Success & Failure ─────────────────────────────────────────────────────
    /// <summary>Tiêu chí xác định "thành công" — Evaluator dùng để verify kết quả.</summary>
    public required SuccessCriterion[] SuccessCriteria { get; init; }

    /// <summary>Các lỗi đã biết — phục vụ fallback routing và Reflector context.</summary>
    public required string[] KnownFailures { get; init; }

    // ── Knowledge ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Lessons đã được tích lũy vào Playbook này qua các version.
    /// Chỉ là summary — Experience gốc vẫn được giữ nguyên.
    /// </summary>
    public required string[] Lessons { get; init; }

    // ── Evaluation ────────────────────────────────────────────────────────────
    public required PlaybookEvaluationPolicy EvaluationPolicy { get; init; }

    /// <summary>Báo cáo evaluation của version này — lưu cùng Playbook, không mutate.</summary>
    public EvaluationReport? LastEvaluationReport { get; init; }

    // ── Versioning ────────────────────────────────────────────────────────────
    public required string? ParentVersion { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public DateTimeOffset? VerifiedAt { get; init; }

    // ── BM25 corpus ───────────────────────────────────────────────────────────
    /// <summary>
    /// Text corpus dùng cho T3 BM25 indexing.
    /// Concatenate của Intent + Lessons + Rules — tự động tính khi build.
    /// </summary>
    public string Corpus => string.Join(" ", new[] { Intent }
        .Concat(Lessons)
        .Concat(Rules)
        .Concat(Contexts));

    // ── Factory ───────────────────────────────────────────────────────────────
    /// <summary>Tạo Playbook Draft mới — điểm khởi đầu của learning loop.</summary>
    public static Playbook CreateDraft(string id, string intent, string[] contexts) => new()
    {
        Id              = id,
        Version         = 1,
        State           = PlaybookState.Draft,
        Intent          = intent,
        Contexts        = contexts,
        Preconditions   = [],
        Strategy        = [],
        Rules           = [],
        Fallbacks       = [],
        SuccessCriteria = [],
        KnownFailures   = [],
        Lessons         = [],
        EvaluationPolicy = PlaybookEvaluationPolicy.Default,
        ParentVersion   = null,
        CreatedAt       = DateTimeOffset.UtcNow,
    };

    /// <summary>
    /// Tạo version mới kế thừa từ version hiện tại — parent trỏ về version cũ.
    /// Version cũ KHÔNG bị overwrite (SC-11).
    /// </summary>
    public Playbook BumpVersion(PlaybookState newState) => this with
    {
        Version       = Version + 1,
        State         = newState,
        ParentVersion = $"{Id}_v{Version}",
        UpdatedAt     = DateTimeOffset.UtcNow,
        LastEvaluationReport = null,   // reset — version mới cần evaluate lại
    };

    /// <summary>Transition state — tạo immutable copy mới, giữ version.</summary>
    public Playbook WithState(PlaybookState state) => this with
    {
        State     = state,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
