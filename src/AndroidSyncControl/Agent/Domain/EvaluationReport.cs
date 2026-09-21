namespace AndroidSyncControl.Agent.Domain;

/// <summary>
/// Báo cáo đánh giá đa tiêu chí từ <c>IPlaybookEvaluator</c>.
/// <para>
/// Weights: Correctness 40% | Stability 25% | SideEffects 20% | Speed 15%.
/// Correctness là hard gate — dù các criteria khác cao, nếu correctness thấp thì vẫn FAIL.
/// </para>
/// </summary>
public sealed record EvaluationReport(
    string PlaybookId,
    int Version,

    /// <summary>Tỷ lệ kết quả thực sự đúng với SuccessCriteria (0.0 → 1.0).</summary>
    float CorrectnessScore,

    /// <summary>Tỷ lệ run không crash / không unhandled exception (0.0 → 1.0).</summary>
    float StabilityScore,

    /// <summary>Score hiệu năng thời gian (1.0 = nhanh, 0.0 = chậm vượt ngưỡng).</summary>
    float SpeedScore,

    /// <summary>Score không có side effects không mong muốn (0.0 → 1.0).</summary>
    float SideEffectScore,

    /// <summary>Tỷ lệ test cases đã pass (0.0 → 1.0).</summary>
    float TestPassRate,

    /// <summary>Số lần regression phát hiện được so với version trước.</summary>
    int RegressionCount,

    DateTimeOffset EvaluatedAt)
{
    /// <summary>
    /// Weighted total = Correctness×0.4 + Stability×0.25 + SideEffect×0.2 + Speed×0.15.
    /// </summary>
    public float WeightedTotal =>
        CorrectnessScore * 0.40f +
        StabilityScore   * 0.25f +
        SideEffectScore  * 0.20f +
        SpeedScore       * 0.15f;

    /// <summary>
    /// True nếu EvaluationReport vượt qua tất cả gates trong <paramref name="policy"/>.
    /// </summary>
    public bool IsVerified(PlaybookEvaluationPolicy policy) =>
        WeightedTotal    >= policy.MinWeightedScore &&
        CorrectnessScore >= policy.MinCorrectnessScore &&   // hard gate
        StabilityScore   >= policy.MinStabilityScore &&
        TestPassRate     >= policy.RequiredTestPassRate &&
        RegressionCount  <= policy.MaxRegressionCount;
}
