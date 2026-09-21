using AndroidSyncControl.Agent.Abstractions;
using AndroidSyncControl.Agent.Domain;

namespace AndroidSyncControl.Agent.Learning;

/// <summary>
/// Phân tích causal chain trong <see cref="Experience"/> và chắt lọc <see cref="Lesson"/>.
/// <para>
/// SC-11: Lesson là HYPOTHESIS, không phải fact. Một lần chạy thành công
/// không đủ để tạo rule — cần nhiều experience xác nhận qua Evaluator.
/// </para>
/// Patterns hiện tại detect được:
/// <list type="bullet">
/// <item>ElementNotFound + UIState=Loading + RecoveryAttempt=success → "ui_readiness"</item>
/// <item>Error contains "timeout" → "timing"</item>
/// <item>RecoveryAttempts.Count > 0 → "fallback"</item>
/// <item>Consecutive FAIL steps → "precondition" (app chưa sẵn sàng)</item>
/// </list>
/// </summary>
public sealed class AgentReflector : IAgentReflector
{
    public Task<ReflectionResult> ReflectAsync(Experience experience, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var lessons = new List<Lesson>();
        var suggestedPreconditions = new List<string>();
        string failureCause = DetermineFailureCause(experience.Traces, lessons, suggestedPreconditions);

        // Deduplicate lessons by category
        var deduped = lessons
            .GroupBy(l => l.Category)
            .Select(g => g.First())
            .ToList();

        return Task.FromResult(new ReflectionResult(
            ExperienceId: experience.Id,
            Lessons: deduped,
            FailureCause: failureCause,
            SuggestedPreconditions: suggestedPreconditions.Distinct().ToList(),
            ReflectedAt: DateTimeOffset.UtcNow));
    }

    // ── Causal chain analysis ─────────────────────────────────────────────────

    private static string DetermineFailureCause(
        IReadOnlyList<ExecutionTrace> traces,
        List<Lesson> lessons,
        List<string> preconditions)
    {
        if (traces.Count == 0)
            return "No traces recorded — executor may have crashed before first step.";

        var failedTraces = traces.Where(t => t.Error is not null).ToList();
        if (failedTraces.Count == 0)
            return "All steps succeeded — no failure to analyze.";

        var causes = new List<string>();

        foreach (var trace in failedTraces)
        {
            // Pattern: ElementNotFound + UI was Loading → timing issue
            if (IsElementNotFound(trace) && trace.UiState == "Loading")
            {
                string lessonId = $"lesson_{Guid.NewGuid():N}";
                lessons.Add(new Lesson(
                    Id: lessonId,
                    Summary: "UI element not visible immediately after app launch — dynamic wait required, not fixed delay.",
                    Category: "ui_readiness",
                    DerivedFromExperienceId: "",   // caller patches this
                    ExtractedAt: DateTimeOffset.UtcNow));
                preconditions.Add("wait_for_ui_stable");
                causes.Add($"Step '{trace.StepName}': ElementNotFound while UI was still Loading.");
            }
            // Pattern: timeout in error
            else if (trace.Error?.Contains("timeout", StringComparison.OrdinalIgnoreCase) == true)
            {
                string lessonId = $"lesson_{Guid.NewGuid():N}";
                lessons.Add(new Lesson(
                    Id: lessonId,
                    Summary: "Operation timed out — consider increasing timeout or verifying element presence first.",
                    Category: "timing",
                    DerivedFromExperienceId: "",
                    ExtractedAt: DateTimeOffset.UtcNow));
                causes.Add($"Step '{trace.StepName}': Timeout — {trace.Error}");
            }
            // Pattern: recovery was attempted and succeeded → fallback exists but not in Playbook
            else if (trace.RecoveryAttempts.Count > 0)
            {
                string lessonId = $"lesson_{Guid.NewGuid():N}";
                lessons.Add(new Lesson(
                    Id: lessonId,
                    Summary: $"Step '{trace.StepName}' required recovery attempts: {string.Join(", ", trace.RecoveryAttempts)}. Add as explicit fallback.",
                    Category: "fallback",
                    DerivedFromExperienceId: "",
                    ExtractedAt: DateTimeOffset.UtcNow));
                causes.Add($"Step '{trace.StepName}' needed recovery: {string.Join(", ", trace.RecoveryAttempts)}.");
            }
            else
            {
                causes.Add($"Step '{trace.StepName}' failed: {trace.Error}");
            }
        }

        // Detect consecutive failures early → likely precondition not met
        if (failedTraces.Count >= 2)
        {
            lessons.Add(new Lesson(
                Id: $"lesson_{Guid.NewGuid():N}",
                Summary: "Multiple consecutive failures — app or device may not be in expected state. Add precondition check.",
                Category: "precondition",
                DerivedFromExperienceId: "",
                ExtractedAt: DateTimeOffset.UtcNow));
            preconditions.Add("verify_app_state_before_execution");
        }

        // Patch experienceId into all lessons
        for (int i = 0; i < lessons.Count; i++)
            lessons[i] = lessons[i] with { DerivedFromExperienceId = "" };   // caller patches

        return string.Join(" | ", causes);
    }

    private static bool IsElementNotFound(ExecutionTrace t) =>
        t.Error?.Contains("ElementNotFound", StringComparison.OrdinalIgnoreCase) == true ||
        t.Error?.Contains("element not found", StringComparison.OrdinalIgnoreCase) == true ||
        t.Error?.Contains("no such element", StringComparison.OrdinalIgnoreCase) == true;
}
