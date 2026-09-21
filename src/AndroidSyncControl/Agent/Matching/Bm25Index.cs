using System.Text.RegularExpressions;

namespace AndroidSyncControl.Agent.Matching;

/// <summary>
/// BM25 (Okapi BM25) scoring engine — thuần C#, zero NuGet.
/// <para>
/// Parameters (Robertson et al. defaults):
/// <br/>• k1 = 1.5 — term saturation. Tăng k1 → term frequency ảnh hưởng nhiều hơn.
/// <br/>• b  = 0.75 — length normalization. b=1.0 = normalize hoàn toàn, b=0 = bỏ qua độ dài.
/// </para>
/// </summary>
internal sealed partial class Bm25Index
{
    private const float K1 = 1.5f;
    private const float B  = 0.75f;

    // corpus[docId] = tokenized words
    private readonly Dictionary<string, string[]> _docs = new(StringComparer.OrdinalIgnoreCase);
    // idf[term] = ln((N - df + 0.5) / (df + 0.5))
    private readonly Dictionary<string, float> _idf = new(StringComparer.OrdinalIgnoreCase);
    private float _avgDocLen;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Xây dựng lại toàn bộ index từ corpus mới.</summary>
    public void Build(IReadOnlyDictionary<string, string> corpus)
    {
        _docs.Clear();
        _idf.Clear();

        foreach (var (id, text) in corpus)
            _docs[id] = Tokenize(text);

        int n = _docs.Count;
        if (n == 0) return;

        _avgDocLen = _docs.Values.Average(d => (float)d.Length);

        // df[term] = number of documents containing term
        var df = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var tokens in _docs.Values)
        foreach (var t in tokens.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            df.TryGetValue(t, out int count);
            df[t] = count + 1;
        }

        foreach (var (term, docFreq) in df)
        {
            // IDF(t) = ln((N - df + 0.5) / (df + 0.5)) — clamped to 0
            float idf = MathF.Log((n - docFreq + 0.5f) / (docFreq + 0.5f) + 1f);
            _idf[term] = MathF.Max(0f, idf);
        }
    }

    /// <summary>
    /// Tính BM25 score của <paramref name="query"/> với document <paramref name="docId"/>.
    /// Trả về 0.0 nếu docId không tồn tại hoặc index trống.
    /// </summary>
    public float Score(string query, string docId)
    {
        if (!_docs.TryGetValue(docId, out var docTokens) || docTokens.Length == 0)
            return 0f;

        var queryTerms = Tokenize(query);
        if (queryTerms.Length == 0) return 0f;

        float docLen = docTokens.Length;
        var tf = BuildTermFrequency(docTokens);

        float score = 0f;
        foreach (var term in queryTerms.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_idf.TryGetValue(term, out float idf)) continue;
            tf.TryGetValue(term, out int termFreq);

            // BM25 term score
            float numerator   = termFreq * (K1 + 1f);
            float denominator = termFreq + K1 * (1f - B + B * docLen / _avgDocLen);
            score += idf * (numerator / denominator);
        }

        return score;
    }

    /// <summary>
    /// Score tất cả documents và trả về danh sách đã sắp xếp giảm dần.
    /// </summary>
    public IReadOnlyList<(string DocId, float Score)> RankAll(string query)
    {
        if (_docs.Count == 0) return [];

        return _docs.Keys
            .Select(id => (DocId: id, Score: Score(query, id)))
            .Where(x => x.Score > 0f)
            .OrderByDescending(x => x.Score)
            .ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Lowercase + split on non-alphanumeric. No stemming needed for tag-heavy corpus.</summary>
    internal static string[] Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return TokenizeRegex()
            .Split(text.ToLowerInvariant())
            .Where(t => t.Length > 1)
            .ToArray();
    }

    private static Dictionary<string, int> BuildTermFrequency(string[] tokens)
    {
        var tf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tokens)
        {
            tf.TryGetValue(t, out int c);
            tf[t] = c + 1;
        }
        return tf;
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex TokenizeRegex();
}
