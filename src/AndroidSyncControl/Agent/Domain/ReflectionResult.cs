namespace AndroidSyncControl.Agent.Domain;

/// <summary>
/// Output của <c>IAgentReflector</c> sau khi phân tích một <see cref="Experience"/>.
/// <para>
/// Reflector chỉ phân tích — không tự quyết định workflow tiếp theo.
/// AgentOrchestrator nhận ReflectionResult và chuyển sang IPlaybookOptimizer.
/// </para>
/// </summary>
public sealed record ReflectionResult(
    string ExperienceId,

    /// <summary>Danh sách bài học chắt lọc được từ causal chain analysis.</summary>
    IReadOnlyList<Lesson> Lessons,

    /// <summary>
    /// Mô tả nguyên nhân gốc (root cause) được Reflector suy ra.
    /// Ví dụ: "UI element not yet rendered when tap was attempted — timing issue."
    /// </summary>
    string FailureCause,

    /// <summary>
    /// Preconditions mà Reflector đề xuất thêm vào Playbook để tránh lỗi tương tự.
    /// Ví dụ: ["wait_for_ui_stable", "verify_element_visible_before_tap"].
    /// </summary>
    IReadOnlyList<string> SuggestedPreconditions,

    DateTimeOffset ReflectedAt);
