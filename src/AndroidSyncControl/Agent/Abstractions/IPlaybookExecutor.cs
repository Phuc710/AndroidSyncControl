using AndroidSyncControl.Agent.Domain;
using AndroidSyncControl.Agent.Execution;

namespace AndroidSyncControl.Agent.Abstractions;

/// <summary>
/// Thực thi một Playbook trên thiết bị Android và trả về ExecutionResult.
/// Impl: <c>PlaybookExecutor</c> → wrap <c>AndroidActionEngine</c> → <c>AndroidToolchain</c>.
/// </summary>
public interface IPlaybookExecutor
{
    Task<ExecutionResult> ExecuteAsync(
        Playbook playbook,
        TaskContext ctx,
        CancellationToken ct = default);
}
