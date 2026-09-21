using AndroidSyncControl.Agent.Abstractions;
using AndroidSyncControl.Agent.Domain;

namespace AndroidSyncControl.Agent.Learning;

/// <summary>
/// Tạo hoặc nâng cấp Playbook Candidate từ <see cref="ReflectionResult"/>.
/// <para>
/// Nếu <c>current == null</c> → tạo Draft rồi chuyển sang Candidate.
/// Nếu <c>current != null</c> → bump version, apply lessons vào Strategy/Fallbacks/Preconditions.
/// Output luôn có <see cref="PlaybookState.Candidate"/>.
/// </para>
/// SC-11: Không publish trực tiếp từ đây. Chỉ IPlaybookEvaluator mới được quyết định Verified.
/// </summary>
public sealed class PlaybookOptimizer : IPlaybookOptimizer
{
    public Task<Playbook> OptimizeAsync(
        Playbook? current,
        ReflectionResult reflection,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        Playbook candidate = current is null
            ? BuildFromScratch(reflection)
            : Upgrade(current, reflection);

        return Task.FromResult(candidate);
    }

    // ── Build new Playbook from reflection (no prior version) ─────────────────

    private static Playbook BuildFromScratch(ReflectionResult reflection)
    {
        string id = $"pb_{Guid.NewGuid():N}";
        var draft = Playbook.CreateDraft(id, reflection.ExperienceId, []);

        return draft with
        {
            State           = PlaybookState.Candidate,
            Preconditions   = [..reflection.SuggestedPreconditions],
            Lessons         = reflection.Lessons.Select(l => l.Summary).ToArray(),
            Strategy        = BuildInitialStrategy(reflection),
            Fallbacks       = BuildFallbacks(reflection),
            UpdatedAt       = DateTimeOffset.UtcNow,
        };
    }

    // ── Upgrade existing Playbook by applying new lessons ─────────────────────

    private static Playbook Upgrade(Playbook current, ReflectionResult reflection)
    {
        var bumped = current.BumpVersion(PlaybookState.Candidate);

        // Merge preconditions (union, deduplicated)
        var newPreconditions = current.Preconditions
            .Union(reflection.SuggestedPreconditions, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Merge lessons summaries
        var newLessons = current.Lessons
            .Union(reflection.Lessons.Select(l => l.Summary), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Apply lesson-driven strategy patches
        var newStrategy = ApplyLessonsToStrategy(current.Strategy, reflection.Lessons);
        var newFallbacks = current.Fallbacks
            .Union(BuildFallbacks(reflection), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return bumped with
        {
            Preconditions = newPreconditions,
            Lessons       = newLessons,
            Strategy      = newStrategy,
            Fallbacks     = newFallbacks,
        };
    }

    // ── Strategy / Fallback builders from lessons ──────────────────────────────

    private static string[] BuildInitialStrategy(ReflectionResult reflection)
    {
        var steps = new List<string> { "launch_app" };

        bool needsUiWait = reflection.Lessons.Any(l => l.Category == "ui_readiness");
        if (needsUiWait) steps.Add("wait_for_ui_stable");

        steps.AddRange(["find_element", "perform_action", "verify_result"]);
        return steps.ToArray();
    }

    private static string[] ApplyLessonsToStrategy(string[] current, IReadOnlyList<Lesson> lessons)
    {
        var steps = current.ToList();

        // ui_readiness lesson → ensure wait_for_ui_stable after launch_app
        if (lessons.Any(l => l.Category == "ui_readiness") &&
            !steps.Contains("wait_for_ui_stable", StringComparer.OrdinalIgnoreCase))
        {
            int launchIdx = steps.FindIndex(s =>
                s.Contains("launch", StringComparison.OrdinalIgnoreCase));
            int insertAt = launchIdx >= 0 ? launchIdx + 1 : 0;
            steps.Insert(insertAt, "wait_for_ui_stable");
        }

        // precondition lesson → add verify_app_state at beginning
        if (lessons.Any(l => l.Category == "precondition") &&
            !steps.Any(s => s.Equals("verify_app_state", StringComparison.OrdinalIgnoreCase)))
        {
            steps.Insert(0, "verify_app_state");
        }

        return steps.ToArray();
    }

    private static string[] BuildFallbacks(ReflectionResult reflection) =>
        reflection.Lessons
            .Where(l => l.Category == "fallback")
            .SelectMany(l => ExtractFallbackSteps(l.Summary))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string> ExtractFallbackSteps(string lessonSummary)
    {
        // Simple heuristic: extract known recovery step names from lesson summary
        var knownSteps = new[]
        {
            "refresh_ui", "retry_step", "relaunch_app",
            "wait_500ms_retry", "scroll_and_retry", "clear_focus",
        };
        return knownSteps.Where(s =>
            lessonSummary.Contains(s, StringComparison.OrdinalIgnoreCase));
    }
}
