namespace AndroidSyncControl.Agent.Domain;

/// <summary>
/// Hợp đồng nhiệm vụ đầu vào chuẩn SC-11.1 cho Agent.
/// </summary>
public sealed record AgentTask
{
    /// <summary>Id duy nhất của task. Tự động sinh nếu không truyền.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Ý định người dùng — ví dụ: "Open Chrome and verify that it is running".</summary>
    public required string Intent { get; init; }

    /// <summary>Loại nhiệm vụ (nếu có) — ví dụ: "automation", "bypass", "setup".</summary>
    public string? TaskType { get; init; }

    /// <summary>Điều kiện tiên quyết cần thỏa mãn trước khi chạy.</summary>
    public IReadOnlyList<string> Preconditions { get; init; } = [];

    /// <summary>Tiêu chí thành công mong đợi sau khi thực thi.</summary>
    public IReadOnlyList<string> SuccessCriteria { get; init; } = [];

    /// <summary>Serial của thiết bị Android mục tiêu.</summary>
    public required string DeviceSerial { get; init; }

    /// <summary>Thời điểm tạo task.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
