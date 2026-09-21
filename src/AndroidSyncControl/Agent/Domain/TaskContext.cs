namespace AndroidSyncControl.Agent.Domain;

/// <summary>
/// Ngữ cảnh của một task mà Agent nhận được — đầu vào của <c>IPlaybookMatcher</c>.
/// </summary>
public sealed record TaskContext(
    /// <summary>Câu mô tả intent nguyên gốc — ví dụ: "Configure Chrome account".</summary>
    string RawIntent,

    /// <summary>
    /// Tags đã được extract từ RawIntent — dùng cho T2 (Jaccard overlap).
    /// Ví dụ: ["chrome", "android", "configure", "account"].
    /// </summary>
    string[] ExtractedTags,

    /// <summary>
    /// Preconditions cần thỏa mãn — ví dụ: ["device_connected", "chrome_installed"].
    /// Matcher kiểm tra trước khi reuse Playbook.
    /// </summary>
    string[] RequiredPreconditions,

    /// <summary>Serial number của thiết bị Android đang kết nối.</summary>
    string DeviceSerial,

    /// <summary>
    /// Properties thiết bị truy vấn runtime qua ADB.
    /// Key ví dụ: "ro.build.version.release", "ro.product.model".
    /// </summary>
    IReadOnlyDictionary<string, string> DeviceProperties);
