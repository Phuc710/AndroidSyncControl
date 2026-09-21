using AndroidSyncControl.Agent.Abstractions;
using AndroidSyncControl.Agent.Domain;
using AndroidSyncControl.Agent.Execution;

namespace AndroidSyncControl.Agent.Evaluation;

/// <summary>
/// Chạy test cases và sinh <see cref="EvaluationReport"/> để AgentOrchestrator quyết định
/// có nâng Playbook lên <see cref="PlaybookState.Verified"/> hay không.
/// <para>
/// SC-11 Hard Gate: Evaluator KHÔNG tự thay đổi state của Playbook.
/// Nó chỉ trả về EvaluationReport — caller quyết định transition.
/// </para>
/// <para>
/// Weights: Correctness 40% | Stability 25% | SideEffects 20% | Speed 15%.
/// </para>
/// </summary>
public sealed class PlaybookEvaluator : IPlaybookEvaluator
{
    private readonly IPlaybookExecutor _executor;

    // SpeedScore = 1.0 nếu hoàn thành trong SpeedBudget, giảm tuyến tính đến 0.0
    private static readonly TimeSpan SpeedBudget = TimeSpan.FromSeconds(30);

    public PlaybookEvaluator(IPlaybookExecutor executor)
    {
        _executor = executor;
    }

    public async Task<EvaluationReport> EvaluateAsync(
        Playbook candidate,
        IEnumerable<TestCase> testCases,
        CancellationToken ct = default)
    {
        var cases = testCases.ToList();
        if (cases.Count == 0)
            throw new ArgumentException("At least one test case required for evaluation.", nameof(testCases));

        int totalTests     = cases.Count;
        int passedTests    = 0;
        int crashCount     = 0;
        int sideEffectHits = 0;
        int regressionCount = 0;
        float totalSpeed   = 0f;
        float totalCorrectness = 0f;

        foreach (var tc in cases)
        {
            ct.ThrowIfCancellationRequested();

            var start = DateTimeOffset.UtcNow;
            ExecutionResult result;
            try
            {
                result = await _executor.ExecuteAsync(candidate, tc.Context, ct);
            }
            catch (Exception ex)
            {
                // Unhandled crash → stability hit
                crashCount++;
                continue;
            }

            TimeSpan elapsed = DateTimeOffset.UtcNow - start;

            // Correctness: did SuccessCriteria pass?
            bool correct = EvaluateSuccessCriteria(result, tc.ExpectedCriteria);
            if (correct)
            {
                passedTests++;
                totalCorrectness += 1f;
            }

            // Stability: result had no crash/exception
            if (result.HadUnexpectedException) crashCount++;

            // Side effects
            if (result.HasUnexpectedSideEffect) sideEffectHits++;

            // Regression: steps that were OK in previous version but now fail
            regressionCount += result.RegressionStepCount;

            // Speed score
            totalSpeed += ComputeSpeedScore(elapsed);
        }

        int evaluated = totalTests - crashCount;   // runs that got past crash phase
        float safeDiv(float num, int den) => den == 0 ? 0f : num / den;

        float correctnessScore = safeDiv(totalCorrectness, totalTests);
        float stabilityScore   = safeDiv(totalTests - crashCount, totalTests);
        float sideEffectScore  = safeDiv(totalTests - sideEffectHits, totalTests);
        float speedScore       = safeDiv(totalSpeed, Math.Max(1, evaluated));
        float testPassRate     = safeDiv(passedTests, totalTests);

        return new EvaluationReport(
            PlaybookId:       candidate.Id,
            Version:          candidate.Version,
            CorrectnessScore: correctnessScore,
            StabilityScore:   stabilityScore,
            SpeedScore:       speedScore,
            SideEffectScore:  sideEffectScore,
            TestPassRate:     testPassRate,
            RegressionCount:  regressionCount,
            EvaluatedAt:      DateTimeOffset.UtcNow);
    }

    // ── SuccessCriteria evaluation ────────────────────────────────────────────

    private static bool EvaluateSuccessCriteria(
        ExecutionResult result,
        IReadOnlyList<SuccessCriterion> criteria)
    {
        foreach (var criterion in criteria)
        {
            bool passed = criterion.Type switch
            {
                "app_running"       => result.ObservedState.TryGetValue("running_package", out var pkg)
                                      && criterion.Parameters.TryGetValue("package", out var expected)
                                      && pkg == expected,

                "ui_element_exists" => result.ObservedState.TryGetValue("element_found", out var found)
                                      && found == "true",

                "text_contains"     => result.ObservedState.TryGetValue("visible_text", out var text)
                                      && criterion.Parameters.TryGetValue("value", out var needle)
                                      && text.Contains(needle, StringComparison.OrdinalIgnoreCase),

                _ => false,    // unknown criterion type → conservative: fail
            };

            if (!passed) return false;
        }
        return true;
    }

    // ── Speed scoring ─────────────────────────────────────────────────────────

    private static float ComputeSpeedScore(TimeSpan elapsed)
    {
        if (elapsed <= SpeedBudget) return 1.0f;
        // Linear decay: 2× budget → 0.0
        double ratio = elapsed.TotalSeconds / SpeedBudget.TotalSeconds;
        return (float)Math.Max(0.0, 2.0 - ratio);
    }
}
