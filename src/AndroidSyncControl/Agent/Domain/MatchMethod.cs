namespace AndroidSyncControl.Agent.Domain;

/// <summary>
/// Phân biệt tier nào trong 3-tier cascade của IPlaybookMatcher đã sinh ra match.
/// </summary>
public enum MatchMethod
{
    /// <summary>T1 — Exact taskId match (confidence = 1.0).</summary>
    Exact,

    /// <summary>T2 — Jaccard overlap của tags + preconditions.</summary>
    Tags,

    /// <summary>T3 — BM25 scoring trên intent / lessons / rules text.</summary>
    Bm25,

    /// <summary>Không tìm được match nào đủ threshold — Agent cần Explore.</summary>
    None,
}
