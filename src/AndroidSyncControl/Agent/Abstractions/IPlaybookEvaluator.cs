using AndroidSyncControl.Agent.Domain;

namespace AndroidSyncControl.Agent.Abstractions;

/// <summary>
/// Chạy test cases và đánh giá Playbook Candidate theo <see cref="PlaybookEvaluationPolicy"/>.
/// <para>
/// SC-11 Contract: Chỉ <c>IPlaybookEvaluator</c> mới được phép quyết định
/// chuyển Playbook sang <see cref="PlaybookState.Verified"/>.
/// AgentExecutor, Reflector, Optimizer đều KHÔNG được tự nâng state lên Verified.
/// </para>
/// </summary>
public interface IPlaybookEvaluator
{
    /// <summary>
    /// Chạy <paramref name="testCases"/> với <paramref name="candidate"/> và trả về báo cáo.
    /// Caller (AgentOrchestrator) dùng <see cref="EvaluationReport.IsVerified"/> để quyết định publish.
    /// </summary>
    Task<EvaluationReport> EvaluateAsync(
        Playbook candidate,
        IEnumerable<TestCase> testCases,
        CancellationToken ct = default);
}
