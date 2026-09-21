using AndroidSyncControl.Agent.Domain;

namespace AndroidSyncControl.Agent.Abstractions;

/// <summary>
/// 3-tier cascade matcher: T1 Exact → T2 Tags → T3 BM25.
/// <para>
/// BM25 không tự quyết định execute — chỉ trả về <see cref="MatchResult"/>.
/// AgentOrchestrator dùng <see cref="MatchResult.ShouldReuse"/> để quyết định.
/// </para>
/// Impl v1: <c>Bm25PlaybookMatcher</c>.
/// Impl v2 (sau): <c>EmbeddingPlaybookMatcher</c> hoặc <c>HybridPlaybookMatcher</c>.
/// Interface không thay đổi — AgentOrchestrator không cần sửa.
/// </summary>
public interface IPlaybookMatcher
{
    Task<MatchResult> FindMatchingAsync(TaskContext context, CancellationToken ct = default);
}
