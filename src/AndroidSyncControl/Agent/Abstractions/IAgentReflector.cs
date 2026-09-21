using AndroidSyncControl.Agent.Domain;

namespace AndroidSyncControl.Agent.Abstractions;

/// <summary>
/// Phân tích <see cref="Experience"/> và chắt lọc <see cref="Lesson"/>.
/// <para>
/// Contract: Reflector chỉ phân tích — KHÔNG quyết định workflow tiếp theo.
/// Không được phép trigger optimize/evaluate từ bên trong Reflector.
/// AgentOrchestrator nhận ReflectionResult và điều phối sang IPlaybookOptimizer.
/// </para>
/// </summary>
public interface IAgentReflector
{
    Task<ReflectionResult> ReflectAsync(Experience experience, CancellationToken ct = default);
}
