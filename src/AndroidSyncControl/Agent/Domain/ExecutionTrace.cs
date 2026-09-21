namespace AndroidSyncControl.Agent.Domain;

/// <summary>
/// Raw trace của một bước execution — do <c>IPlaybookExecutor</c> ghi lại.
/// <para>
/// QUAN TRỌNG: ExecutionTrace chỉ ghi <b>sự kiện</b> (what happened).
/// <c>IAgentReflector</c> mới chịu trách nhiệm suy ra <b>nguyên nhân</b> (why it happened).
/// Không để Executor tự viết "reason" — tách biệt evidence và AI interpretation.
/// </para>
/// </summary>
public sealed record ExecutionTrace(
    /// <summary>Tên bước — ví dụ: "find_element", "tap", "wait_for_ui".</summary>
    string StepName,

    /// <summary>Input của bước (resourceId, coordinates, text, v.v.).</summary>
    object? Input,

    /// <summary>Output raw trả về — null nếu step không có return value.</summary>
    object? Output,

    /// <summary>Error message nếu bước thất bại — null nếu thành công.</summary>
    string? Error,

    /// <summary>Thời gian thực thi bước — phục vụ SpeedScore trong EvaluationReport.</summary>
    TimeSpan Duration,

    /// <summary>
    /// Trạng thái UI quan sát được khi bước chạy.
    /// Ví dụ: "Loading", "Ready", "Crashed", "Unknown".
    /// </summary>
    string UiState,

    /// <summary>
    /// Các recovery attempt đã thử nếu bước ban đầu thất bại.
    /// Ví dụ: ["wait_500ms_retry", "relaunch_and_retry"].
    /// </summary>
    IReadOnlyList<string> RecoveryAttempts);
