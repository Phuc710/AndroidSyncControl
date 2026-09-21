using AndroidSyncControl.Agent.Abstractions;
using AndroidSyncControl.Agent.Domain;

namespace AndroidSyncControl.Agent.Matching;

/// <summary>
/// 3-tier cascade IPlaybookMatcher impl — BM25/TF-IDF, thuần C#.
/// <para>
/// Cascade:
/// <br/>T1 Exact taskId → confidence 1.0 nếu id khớp chính xác
/// <br/>T2 Jaccard tag overlap → threshold 0.6
/// <br/>T3 BM25 intent/lessons/rules → threshold 0.5 (raw BM25 score, normalized)
/// </para>
/// <para>
/// Gate cuối (ShouldReuse): confidence >= 0.75 + preconditions pass + state Verified/Published.
/// BM25 KHÔNG tự quyết execute — chỉ trả về MatchResult, AgentOrchestrator quyết định.
/// </para>
/// </summary>
public sealed class Bm25PlaybookMatcher : IPlaybookMatcher
{
    private const float T2JaccardThreshold  = 0.60f;
    private const float T3Bm25Threshold     = 0.25f;
    private const float T3NormalizationCap  = 2.0f;    // BM25 raw scores >2.0 → cap to 1.0

    private readonly IPlaybookRepository _repo;
    private readonly Bm25Index _index = new();
    private bool _indexDirty = true;

    // docId → playbook mapping maintained alongside index
    private Dictionary<string, Playbook> _indexedPlaybooks = new();

    public Bm25PlaybookMatcher(IPlaybookRepository repo)
    {
        _repo = repo;
    }

    // ── IPlaybookMatcher ──────────────────────────────────────────────────────

    public async Task<MatchResult> FindMatchingAsync(TaskContext context, CancellationToken ct = default)
    {
        var candidates = await LoadCandidatePlaybooksAsync(ct);
        if (candidates.Count == 0) return MatchResult.NoMatch;

        await EnsureIndexBuiltAsync(candidates, ct);

        // ── T1: Exact taskId ──────────────────────────────────────────────────
        var t1 = TryExactMatch(context, candidates);
        if (t1 is not null)
        {
            bool precPass = CheckPreconditions(t1, context);
            return new MatchResult(
                BestMatch: t1,
                ConfidenceScore: 1.0f,
                Method: MatchMethod.Exact,
                PreconditionsPassed: precPass,
                Candidates: [new MatchCandidate(t1, 1.0f, MatchMethod.Exact,
                    [new MatchEvidence("Exact taskId match", 1.0f, "T1_ExactId")])]);
        }

        // ── T2: Jaccard tag overlap ────────────────────────────────────────────
        var t2Results = ScoreByTags(context, candidates);
        var t2Best = t2Results.FirstOrDefault();

        if (t2Best.Score >= T2JaccardThreshold)
        {
            bool precPass = CheckPreconditions(t2Best.Playbook, context);
            var allCandidates = t2Results
                .Select(r => new MatchCandidate(r.Playbook, r.Score, MatchMethod.Tags, r.Evidences))
                .ToList();
            return new MatchResult(
                BestMatch: t2Best.Playbook,
                ConfidenceScore: t2Best.Score,
                Method: MatchMethod.Tags,
                PreconditionsPassed: precPass,
                Candidates: allCandidates);
        }

        // ── T3: BM25 ──────────────────────────────────────────────────────────
        var t3Results = ScoreByBm25(context, candidates);
        var t3Best = t3Results.FirstOrDefault();

        if (t3Best.Score >= T3Bm25Threshold)
        {
            bool precPass = CheckPreconditions(t3Best.Playbook, context);
            var allCandidates = t3Results
                .Select(r => new MatchCandidate(r.Playbook, r.Score, MatchMethod.Bm25, r.Evidences))
                .ToList();
            return new MatchResult(
                BestMatch: t3Best.Playbook,
                ConfidenceScore: t3Best.Score,
                Method: MatchMethod.Bm25,
                PreconditionsPassed: precPass,
                Candidates: allCandidates);
        }

        return MatchResult.NoMatch;
    }

    /// <summary>Gọi sau khi có Playbook mới được publish — rebuild BM25 index.</summary>
    public void InvalidateIndex() => _indexDirty = true;

    // ── T1 ────────────────────────────────────────────────────────────────────

    private static Playbook? TryExactMatch(TaskContext ctx, IReadOnlyList<Playbook> candidates)
    {
        // Exact match nếu RawIntent sau normalize == Playbook.Id hoặc Intent
        string normalizedIntent = Normalize(ctx.RawIntent);
        return candidates.FirstOrDefault(p =>
            string.Equals(Normalize(p.Id), normalizedIntent, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Normalize(p.Intent), normalizedIntent, StringComparison.OrdinalIgnoreCase));
    }

    // ── T2 ────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<(Playbook Playbook, float Score, IReadOnlyList<MatchEvidence> Evidences)>
        ScoreByTags(TaskContext ctx, IReadOnlyList<Playbook> candidates)
    {
        var queryTags = ctx.ExtractedTags
            .Select(t => t.ToLowerInvariant())
            .ToHashSet();

        return candidates
            .Select(p =>
            {
                var pbTags = p.Contexts.Select(t => t.ToLowerInvariant()).ToHashSet();
                int intersect = queryTags.Intersect(pbTags).Count();
                int union     = queryTags.Union(pbTags).Count();
                float jaccard = union == 0 ? 0f : (float)intersect / union;

                var commonTags = queryTags.Intersect(pbTags).ToArray();
                var evidences = new List<MatchEvidence>
                {
                    new($"Tag overlap: [{string.Join(", ", commonTags)}] — Jaccard {jaccard:F2}",
                        jaccard, "T2_TagJaccard"),
                };
                return (Playbook: p, Score: jaccard, Evidences: (IReadOnlyList<MatchEvidence>)evidences);
            })
            .Where(x => x.Score > 0f)
            .OrderByDescending(x => x.Score)
            .ToList();
    }

    // ── T3 ────────────────────────────────────────────────────────────────────

    private IReadOnlyList<(Playbook Playbook, float Score, IReadOnlyList<MatchEvidence> Evidences)>
        ScoreByBm25(TaskContext ctx, IReadOnlyList<Playbook> candidates)
    {
        var ranked = _index.RankAll(ctx.RawIntent);
        var results = new List<(Playbook, float, IReadOnlyList<MatchEvidence>)>();

        foreach (var (docId, rawScore) in ranked)
        {
            if (!_indexedPlaybooks.TryGetValue(docId, out var pb)) continue;

            // Normalize BM25 raw score to [0, 1] — cap at T3NormalizationCap
            float normalized = MathF.Min(rawScore / T3NormalizationCap, 1.0f);

            var evidences = new List<MatchEvidence>
            {
                new($"BM25 score {rawScore:F3} (norm {normalized:F2}) for intent '{ctx.RawIntent}'",
                    normalized, "T3_BM25"),
            };
            results.Add((pb, normalized, evidences));
        }

        return results
            .OrderByDescending(x => x.Item2)
            .ToList();
    }

    // ── Preconditions ─────────────────────────────────────────────────────────

    private static bool CheckPreconditions(Playbook pb, TaskContext ctx)
    {
        if (pb.Preconditions.Length == 0) return true;

        // Mọi precondition của Playbook phải có trong RequiredPreconditions của context
        // (context đã verify runtime — device_connected, app_installed, v.v.)
        var satisfied = ctx.RequiredPreconditions
            .Select(p => p.ToLowerInvariant())
            .ToHashSet();

        return pb.Preconditions.All(p => satisfied.Contains(p.ToLowerInvariant()));
    }

    // ── Index Management ──────────────────────────────────────────────────────

    private async Task EnsureIndexBuiltAsync(IReadOnlyList<Playbook> playbooks, CancellationToken ct)
    {
        if (!_indexDirty) return;

        _indexedPlaybooks = playbooks.ToDictionary(p => BuildDocId(p), p => p);
        var corpus = _indexedPlaybooks.ToDictionary(kv => kv.Key, kv => kv.Value.Corpus);
        _index.Build(corpus);
        _indexDirty = false;

        await Task.CompletedTask;   // reserved for async build if needed
    }

    private async Task<IReadOnlyList<Playbook>> LoadCandidatePlaybooksAsync(CancellationToken ct)
    {
        var verified  = await _repo.GetByStateAsync(PlaybookState.Verified, ct);
        var published = await _repo.GetByStateAsync(PlaybookState.Published, ct);
        return [..verified, ..published];
    }

    private static string BuildDocId(Playbook p) => $"{p.Id}_v{p.Version}";
    private static string Normalize(string s)     => s.Trim().ToLowerInvariant().Replace(" ", "_");
}
