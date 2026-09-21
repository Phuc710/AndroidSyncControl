namespace AndroidSyncControl.Agent.Domain;

/// <summary>
/// Ghi lại toàn bộ một lần thực thi — kể cả khi fail.
/// <para>
/// SC-11 Contract: Experience KHÔNG BAO GIỜ bị xóa sau khi Playbook được publish.
/// Nếu Playbook sau này fail, Agent cần quay lại Experience cũ để hiểu lịch sử.
/// </para>
/// <para>
/// Three-layer separation:
/// <br/>• Experience = "chuyện gì đã xảy ra"
/// <br/>• Lesson = "học được gì" (do IAgentReflector sinh)
/// <br/>• Playbook = "lần sau nên làm thế nào"
/// </para>
/// </summary>
public sealed record Experience(
    string Id,

    /// <summary>Intent nguyên gốc của task đã chạy.</summary>
    string TaskIntent,

    /// <summary>Id của Playbook đã dùng — null nếu đây là lần Explore đầu tiên.</summary>
    string? PlaybookUsed,

    /// <summary>Version của Playbook đã dùng — null nếu PlaybookUsed là null.</summary>
    int? PlaybookVersion,

    /// <summary>"SUCCESS" | "FAIL" | "PARTIAL"</summary>
    string Outcome,

    /// <summary>Raw trace của từng bước — Reflector sẽ phân tích causal chain từ đây.</summary>
    IReadOnlyList<ExecutionTrace> Traces,

    /// <summary>Serial thiết bị — phục vụ device-specific regression analysis.</summary>
    string DeviceSerial,

    DateTimeOffset RecordedAt);
