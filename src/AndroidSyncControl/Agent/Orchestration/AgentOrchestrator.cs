using AndroidSyncControl.Agent.Abstractions;
using AndroidSyncControl.Agent.Domain;
using AndroidSyncControl.Agent.Execution;

namespace AndroidSyncControl.Agent.Orchestration;

/// <summary>
/// Main loop controller — điều phối toàn bộ pipeline học và reuse theo flow đã chốt:
/// <code>
/// RECEIVE TASK
///     ↓
/// MATCH MEMORY (IPlaybookMatcher)
///     ↓
/// PLAYBOOK FOUND?
///   ├── YES → Execute → Verify → Done
///   └── NO  → Explore → Experience → Reflect → Optimize → Evaluate
///                 ↓
///             PASS → Verified → Published → Reuse next time
///             FAIL → Improve (max 3 iterations) → Deprecated if still failing
/// </code>
/// <para>MCP / Tool-calling nằm NGOÀI loop này — đây là internal learning engine.</para>
/// </summary>
public sealed class AgentOrchestrator
{
    private const int MaxLearnIterations = 3;

    private readonly IPlaybookMatcher _matcher;
    private readonly IPlaybookExecutor _executor;
    private readonly IAgentReflector _reflector;
    private readonly IPlaybookOptimizer _optimizer;
    private readonly IPlaybookEvaluator _evaluator;
    private readonly IPlaybookRepository _repo;

    public AgentOrchestrator(
        IPlaybookMatcher   matcher,
        IPlaybookExecutor  executor,
        IAgentReflector    reflector,
        IPlaybookOptimizer optimizer,
        IPlaybookEvaluator evaluator,
        IPlaybookRepository repo)
    {
        _matcher   = matcher;
        _executor  = executor;
        _reflector = reflector;
        _optimizer = optimizer;
        _evaluator = evaluator;
        _repo      = repo;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Entry point — receives a task context and runs the full learning loop.</summary>
    public async Task<OrchestratorResult> ExecuteTaskAsync(
        TaskContext ctx,
        IEnumerable<TestCase>? evaluationTestCases = null,
        CancellationToken ct = default)
    {
        // ── Step 1: Match memory ──────────────────────────────────────────────
        var matchResult = await _matcher.FindMatchingAsync(ctx, ct);

        if (matchResult.ShouldReuse)
        {
            // ── Step 2a: Reuse verified Playbook ─────────────────────────────
            return await ReusePlaybookAsync(matchResult.BestMatch!, ctx, ct);
        }
        else
        {
            // ── Step 2b: Explore and learn ────────────────────────────────────
            return await ExploreAndLearnAsync(ctx, evaluationTestCases, ct);
        }
    }

    // ── Reuse path ────────────────────────────────────────────────────────────

    private async Task<OrchestratorResult> ReusePlaybookAsync(
        Playbook playbook,
        TaskContext ctx,
        CancellationToken ct)
    {
        var result = await _executor.ExecuteAsync(playbook, ctx, ct);
        var experience = BuildExperience(ctx, playbook, result);
        await _repo.AppendExperienceAsync(experience, ct);

        if (!result.Success)
        {
            // Regression detected on Published Playbook — trigger learning for new version
            await TriggerRegressionLearningAsync(playbook, experience, ctx, ct);
        }

        return new OrchestratorResult(
            Success:         result.Success,
            PlaybookUsed:    playbook.Id,
            PlaybookVersion: playbook.Version,
            ExperienceId:    experience.Id,
            Path:            result.Success ? OrchestratorPath.Reused : OrchestratorPath.RegressionDetected);
    }

    // ── Explore & Learn path ──────────────────────────────────────────────────

    private async Task<OrchestratorResult> ExploreAndLearnAsync(
        TaskContext ctx,
        IEnumerable<TestCase>? testCases,
        CancellationToken ct)
    {
        // Execute exploratory playbook (minimal draft just to get experience)
        var exploratoryPlaybook = Playbook.CreateDraft(
            id:       $"explore_{Guid.NewGuid():N}",
            intent:   ctx.RawIntent,
            contexts: ctx.ExtractedTags);

        var result = await _executor.ExecuteAsync(exploratoryPlaybook, ctx, ct);
        var experience = BuildExperience(ctx, null, result);
        await _repo.AppendExperienceAsync(experience, ct);

        // ── Learning loop (max MaxLearnIterations) ────────────────────────────
        Playbook? current = null;
        EvaluationReport? lastReport = null;
        var cases = (testCases ?? []).ToList();

        for (int iteration = 1; iteration <= MaxLearnIterations; iteration++)
        {
            ct.ThrowIfCancellationRequested();

            // Reflect
            var reflection = await _reflector.ReflectAsync(experience, ct);
            PatchLessonExperienceIds(reflection, experience.Id);

            // Optimize → Candidate
            current = await _optimizer.OptimizeAsync(current, reflection, ct);

            // Transition to Testing
            current = current.WithState(PlaybookState.Testing);

            // SC-11.10: Tự động sinh test suite TC-01..TC-07 nếu chưa có
            if (cases.Count == 0)
            {
                cases = StandardTestCaseGenerator.Generate(ctx, current.SuccessCriteria).ToList();
            }

            // Evaluate qua Quality Gate
            lastReport = await _evaluator.EvaluateAsync(current, cases, ct);

            if (lastReport.IsVerified(current.EvaluationPolicy))
            {
                // ── PASS: Verified → Published ────────────────────────────
                var verified  = current.WithState(PlaybookState.Verified) with
                {
                    VerifiedAt            = DateTimeOffset.UtcNow,
                    LastEvaluationReport  = lastReport,
                };
                var published = verified.WithState(PlaybookState.Published);
                await _repo.SaveAsync(published, ct);

                return new OrchestratorResult(
                    Success:         true,
                    PlaybookUsed:    published.Id,
                    PlaybookVersion: published.Version,
                    ExperienceId:    experience.Id,
                    Path:            OrchestratorPath.LearnedAndPublished);
            }

            // FAIL → bump version to Learning, run another iteration
            current = current.BumpVersion(PlaybookState.Learning);

            // Re-execute to get fresh experience for next iteration
            var reRunResult = await _executor.ExecuteAsync(current, ctx, ct);
            experience = BuildExperience(ctx, current, reRunResult);
            await _repo.AppendExperienceAsync(experience, ct);
        }

        // All iterations exhausted — save best candidate as Deprecated
        if (current is not null)
        {
            var deprecated = current.WithState(PlaybookState.Deprecated);
            await _repo.SaveAsync(deprecated, ct);
        }

        return new OrchestratorResult(
            Success:         false,
            PlaybookUsed:    current?.Id,
            PlaybookVersion: current?.Version,
            ExperienceId:    experience.Id,
            Path:            OrchestratorPath.ExhaustedIterations);
    }

    // ── Regression handling ───────────────────────────────────────────────────

    private async Task TriggerRegressionLearningAsync(
        Playbook failing,
        Experience regressionExperience,
        TaskContext ctx,
        CancellationToken ct)
    {
        // Create vN+1 in Learning state — vN stays Published for rollback
        var learningNext = failing.BumpVersion(PlaybookState.Learning);
        await _repo.SaveAsync(learningNext, ct);
        // Note: full learn loop can be triggered by caller passing the new version back to ExecuteTaskAsync
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Experience BuildExperience(
        TaskContext ctx,
        Playbook? playbook,
        ExecutionResult result) => new(
            Id:             $"exp_{Guid.NewGuid():N}",
            TaskIntent:     ctx.RawIntent,
            PlaybookUsed:   playbook?.Id,
            PlaybookVersion:playbook?.Version,
            Outcome:        result.Success ? "SUCCESS" : result.HadUnexpectedException ? "PARTIAL" : "FAIL",
            Traces:         result.Traces,
            DeviceSerial:   ctx.DeviceSerial,
            RecordedAt:     DateTimeOffset.UtcNow);

    /// <summary>
    /// Reflector leaves DerivedFromExperienceId empty — Orchestrator patches it
    /// to avoid circular dependency (Reflector doesn't know experienceId at reflection time).
    /// </summary>
    private static void PatchLessonExperienceIds(ReflectionResult reflection, string experienceId)
    {
        // ReflectionResult is a record — lessons are IReadOnlyList; we can't mutate.
        // Caller can use this utility if they need to persist lessons separately.
        // For now, experienceId is embedded in ReflectionResult.ExperienceId.
        _ = experienceId; // no-op here; patching pattern reserved for persistence layer
    }
}

// ── Result types ──────────────────────────────────────────────────────────────

public enum OrchestratorPath
{
    /// <summary>Found Verified/Published Playbook and reused it.</summary>
    Reused,

    /// <summary>Reused Playbook but failed — regression detected, vN+1 Learning created.</summary>
    RegressionDetected,

    /// <summary>Explored, learned, published. Ready for next reuse.</summary>
    LearnedAndPublished,

    /// <summary>Explored, created Candidate (no test cases provided for formal evaluation).</summary>
    CandidateSaved,

    /// <summary>All learn iterations exhausted — Deprecated.</summary>
    ExhaustedIterations,
}

public sealed record OrchestratorResult(
    bool    Success,
    string? PlaybookUsed,
    int?    PlaybookVersion,
    string  ExperienceId,
    OrchestratorPath Path);
