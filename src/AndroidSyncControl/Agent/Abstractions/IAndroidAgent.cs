using AndroidSyncControl.Agent.Domain;

namespace AndroidSyncControl.Agent.Abstractions;

/// <summary>
/// Giao diện mặt tiền (Facade) chính của hệ sinh thái Agent.
/// Phản ánh toàn bộ vòng đời tác vụ thông minh:
/// Understand -> Plan -> Discover -> Decide -> Execute -> Observe -> Verify -> Learn -> Reuse.
/// </summary>
public interface IAndroidAgent
{
    /// <summary>
    /// Thực thi một <see cref="AgentTask"/> theo toàn bộ vòng đời Agentic.
    /// </summary>
    Task<AgentExecutionResult> ExecuteAsync(AgentTask task, CancellationToken ct = default);
}
