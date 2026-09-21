namespace AndroidSyncControl.Agent.Domain;

/// <summary>
/// State machine cho vòng đời của một Playbook.
/// Allowed transitions:
///   Draft → Learning → Candidate → Testing → Verified → Published → Deprecated
///   Testing -FAIL→ Learning  (tạo version mới, version cũ giữ nguyên)
///   Published -REGRESSION→ (tạo vN+1 Learning, vN vẫn sống để rollback)
/// </summary>
public enum PlaybookState
{
    /// <summary>Mới tạo, chưa có execution data.</summary>
    Draft,

    /// <summary>Đang trong vòng lặp học — đã có experience, chưa đủ để optimize.</summary>
    Learning,

    /// <summary>Đã optimize từ lessons, sẵn sàng đưa vào Testing.</summary>
    Candidate,

    /// <summary>Đang chạy test cases qua IPlaybookEvaluator.</summary>
    Testing,

    /// <summary>Đã vượt qua PlaybookEvaluationPolicy — đủ tiêu chuẩn để reuse.</summary>
    Verified,

    /// <summary>Đã được công nhận chính thức, sẵn sàng cho IPlaybookMatcher reuse.</summary>
    Published,

    /// <summary>Không còn được dùng (app/OS thay đổi), nhưng giữ lại để audit trail.</summary>
    Deprecated,
}
