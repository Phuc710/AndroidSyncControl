using AndroidSyncControl.Agent.Domain;

namespace AndroidSyncControl.Agent.Abstractions;

/// <summary>
/// Nhận <see cref="ReflectionResult"/> từ <c>IAgentReflector</c> và tạo Playbook Candidate mới.
/// <para>
/// Nếu <paramref name="current"/> là null → tạo Draft mới từ lessons.
/// Nếu <paramref name="current"/> không null → bump version, apply lessons vào Strategy/Fallbacks/Preconditions.
/// Output luôn có State = <see cref="PlaybookState.Candidate"/>.
/// </para>
/// </summary>
public interface IPlaybookOptimizer
{
    Task<Playbook> OptimizeAsync(
        Playbook? current,
        ReflectionResult reflection,
        CancellationToken ct = default);
}
