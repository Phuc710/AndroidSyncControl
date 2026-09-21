namespace AndroidSyncControl.Agent.Execution;

/// <summary>
/// Kết quả trả về từ <c>IPlaybookExecutor.ExecuteAsync</c>.
/// </summary>
public sealed record ExecutionResult(
    bool Success,

    /// <summary>Raw traces của từng bước — IAgentReflector sẽ phân tích.</summary>
    IReadOnlyList<AndroidSyncControl.Agent.Domain.ExecutionTrace> Traces,

    /// <summary>
    /// Trạng thái quan sát được sau khi chạy xong — dùng bởi PlaybookEvaluator.
    /// Key ví dụ: "running_package", "element_found", "visible_text".
    /// </summary>
    IReadOnlyDictionary<string, string> ObservedState,

    /// <summary>True nếu có unhandled exception trong quá trình thực thi.</summary>
    bool HadUnexpectedException,

    /// <summary>True nếu phát hiện side effect không mong muốn.</summary>
    bool HasUnexpectedSideEffect,

    /// <summary>Số bước bị regression so với version trước (0 nếu không có baseline).</summary>
    int RegressionStepCount,

    string? ErrorMessage);
