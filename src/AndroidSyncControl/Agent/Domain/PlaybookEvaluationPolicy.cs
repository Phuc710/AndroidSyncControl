namespace AndroidSyncControl.Agent.Domain;

/// <summary>
/// Bộ tiêu chí đa chiều xác định khi nào một Playbook Candidate được nâng lên Verified.
/// Thay thế hoàn toàn MinSuccessRate — một con số duy nhất không đủ để đảm bảo chất lượng.
/// </summary>
/// <param name="MinWeightedScore">
///   Tổng điểm có trọng số tối thiểu (weighted sum của 4 criteria).
///   Default: 0.85
/// </param>
/// <param name="MinCorrectnessScore">
///   Hard gate — Playbook phải cho ra kết quả đúng.
///   Dù các criteria khác cao, nếu correctness < threshold thì vẫn FAIL.
///   Default: 0.90
/// </param>
/// <param name="MinStabilityScore">
///   Tỷ lệ run không có crash / unhandled exception.
///   Default: 0.85
/// </param>
/// <param name="MaxRegressionCount">
///   Số lần tối đa một bước trong Playbook gây regression so với version trước.
///   Default: 0 (zero tolerance)
/// </param>
/// <param name="RequiredTestPassRate">
///   Tỷ lệ test cases phải pass 100%.
///   Default: 1.0
/// </param>
public sealed record PlaybookEvaluationPolicy(
    float MinWeightedScore      = 0.85f,
    float MinCorrectnessScore   = 0.90f,
    float MinStabilityScore     = 0.85f,
    int   MaxRegressionCount    = 0,
    float RequiredTestPassRate  = 1.0f)
{
    /// <summary>Default policy áp dụng cho mọi Playbook nếu không chỉ định riêng.</summary>
    public static readonly PlaybookEvaluationPolicy Default = new();
}
