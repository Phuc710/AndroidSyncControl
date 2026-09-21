namespace AndroidSyncControl.Agent.Domain;

/// <summary>
/// Một bài học được <c>IAgentReflector</c> chắt lọc từ <see cref="Experience"/>.
/// <para>
/// SC-11 Contract: Lesson là <b>hypothesis</b>, không phải fact đã xác minh.
/// Chỉ trở thành rule được reuse sau khi nhiều Playbook version xác nhận và đã qua Evaluator.
/// </para>
/// </summary>
public sealed record Lesson(
    string Id,

    /// <summary>Tóm tắt ngắn gọn — ví dụ: "UI not stable immediately after app launch".</summary>
    string Summary,

    /// <summary>
    /// Phân loại — giúp Optimizer biết áp dụng vào phần nào của Playbook.
    /// Ví dụ: "timing", "ui_readiness", "precondition", "fallback", "element_id".
    /// </summary>
    string Category,

    /// <summary>Trỏ về Experience đã sinh ra Lesson này — không được xóa Experience.</summary>
    string DerivedFromExperienceId,

    /// <summary>Thời điểm Reflector sinh ra Lesson này.</summary>
    DateTimeOffset ExtractedAt);
