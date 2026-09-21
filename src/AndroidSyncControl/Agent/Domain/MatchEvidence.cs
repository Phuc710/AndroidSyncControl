namespace AndroidSyncControl.Agent.Domain;

/// <summary>
/// Một mảnh bằng chứng trong quá trình matching — giúp debug tại sao Agent chọn Playbook nào.
/// Thay vì chỉ thấy "Confidence = 0.82", ta thấy rõ từng nguồn đóng góp.
/// </summary>
public sealed record MatchEvidence(
    /// <summary>Mô tả ngắn — ví dụ: "Tags overlap: [android, camera]".</summary>
    string Reason,

    /// <summary>Score của evidence này (0.0 → 1.0).</summary>
    float Score,

    /// <summary>
    /// Nguồn sinh ra evidence — ví dụ: "T1_ExactId", "T2_TagJaccard", "T3_BM25".
    /// </summary>
    string Source);
