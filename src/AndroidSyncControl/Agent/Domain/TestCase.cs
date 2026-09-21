namespace AndroidSyncControl.Agent.Domain;

/// <summary>
/// Một tiêu chí xác định "thành công" — phần của <see cref="TestCase"/> và <see cref="Playbook.SuccessCriteria"/>.
/// </summary>
public sealed record SuccessCriterion(
    /// <summary>
    /// Loại criterion — ví dụ: "app_running", "ui_element_exists", "text_contains".
    /// </summary>
    string Type,

    /// <summary>Parameters của criterion — ví dụ: { "package": "com.example.app" }.</summary>
    IReadOnlyDictionary<string, string> Parameters);

/// <summary>
/// Một test case chạy qua <c>IPlaybookEvaluator</c> để đánh giá Playbook Candidate.
/// </summary>
public sealed record TestCase(
    string Id,

    /// <summary>Context để thực thi — thiết bị, intent, device props.</summary>
    TaskContext Context,

    /// <summary>Tất cả các tiêu chí phải thỏa mãn sau khi Playbook chạy xong.</summary>
    IReadOnlyList<SuccessCriterion> ExpectedCriteria);
