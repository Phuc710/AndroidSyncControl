using AndroidSyncControl.Agent.Abstractions;
using AndroidSyncControl.Agent.Domain;

namespace AndroidSyncControl.Agent.Execution;

/// <summary>
/// <see cref="IPlaybookExecutor"/> impl — dịch Playbook.Strategy thành AndroidActionEngine calls.
/// Ghi lại từng bước thành <see cref="ExecutionTrace"/> (raw — không suy nguyên nhân).
/// </summary>
public sealed class PlaybookExecutor : IPlaybookExecutor
{
    private const int MaxFallbackAttempts = 2;

    public async Task<ExecutionResult> ExecuteAsync(
        Playbook playbook,
        TaskContext ctx,
        CancellationToken ct = default)
    {
        var engine = new AndroidActionEngine(ctx.DeviceSerial);
        var traces = new List<ExecutionTrace>();
        bool hadException = false;
        bool hadSideEffect = false;
        int regressionSteps = 0;

        // Extract package from DeviceProperties if available
        var stepParams = BuildStepParameters(playbook, ctx);

        try
        {
            foreach (var step in playbook.Strategy)
            {
                ct.ThrowIfCancellationRequested();

                stepParams.TryGetValue(step, out var p);
                var trace = await engine.ExecuteStepAsync(step, p, ct);
                traces.Add(trace);

                // If step failed, attempt fallbacks before giving up
                if (trace.Error is not null)
                {
                    var (recoveredTrace, recovered) = await TryFallbacksAsync(
                        engine, step, p, playbook.Fallbacks, traces, ct);
                    if (!recovered)
                    {
                        // Fatal step failure — stop execution, record remaining steps as skipped
                        break;
                    }
                    traces[^1] = recoveredTrace;
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            hadException = true;
            traces.Add(new ExecutionTrace(
                StepName: "UNHANDLED_EXCEPTION",
                Input: null,
                Output: null,
                Error: ex.Message,
                Duration: TimeSpan.Zero,
                UiState: "Crashed",
                RecoveryAttempts: []));
        }

        bool success = traces.Count > 0 && traces.All(t => t.Error is null);
        var observedState = await CollectObservedStateAsync(engine, ctx, ct);

        return new ExecutionResult(
            Success: success,
            Traces: traces,
            ObservedState: observedState,
            HadUnexpectedException: hadException,
            HasUnexpectedSideEffect: hadSideEffect,
            RegressionStepCount: regressionSteps,
            ErrorMessage: traces.LastOrDefault(t => t.Error is not null)?.Error);
    }

    // ── Fallback handling ─────────────────────────────────────────────────────

    private static async Task<(ExecutionTrace Trace, bool Recovered)> TryFallbacksAsync(
        AndroidActionEngine engine,
        string originalStep,
        IReadOnlyDictionary<string, string>? originalParams,
        string[] fallbacks,
        List<ExecutionTrace> traces,
        CancellationToken ct)
    {
        var attempts = new List<string>();

        for (int i = 0; i < Math.Min(fallbacks.Length, MaxFallbackAttempts); i++)
        {
            string fallback = fallbacks[i];
            attempts.Add(fallback);

            var fbTrace = await engine.ExecuteStepAsync(fallback, null, ct);
            traces.Add(fbTrace);

            if (fbTrace.Error is null)
            {
                // Fallback succeeded — retry original step
                var retryTrace = await engine.ExecuteStepAsync(originalStep, originalParams, ct);
                traces.Add(retryTrace);
                if (retryTrace.Error is null)
                {
                    return (retryTrace with { RecoveryAttempts = attempts }, true);
                }
            }
        }

        return (new ExecutionTrace(originalStep, originalParams, null,
            "All fallbacks exhausted.", TimeSpan.Zero, "Failed", attempts), false);
    }

    // ── Observed state collection ─────────────────────────────────────────────

    private static async Task<IReadOnlyDictionary<string, string>> CollectObservedStateAsync(
        AndroidActionEngine engine,
        TaskContext ctx,
        CancellationToken ct)
    {
        var state = new Dictionary<string, string>();
        try
        {
            var trace = await engine.ExecuteStepAsync(
                "verify_app_state",
                new Dictionary<string, string> { ["package"] = GetPackage(ctx) },
                ct);
            if (trace.Output is string s) state["running_package"] = s == "foreground" ? GetPackage(ctx) : "";
        }
        catch { /* non-fatal */ }
        return state;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> BuildStepParameters(
        Playbook playbook,
        TaskContext ctx)
    {
        // Build a best-effort parameter map from context + playbook metadata
        string pkg = GetPackage(ctx);
        return new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["launch_app"]      = new Dictionary<string, string> { ["package"] = pkg },
            ["force_stop_app"]  = new Dictionary<string, string> { ["package"] = pkg },
            ["verify_app_state"]= new Dictionary<string, string> { ["package"] = pkg },
        };
    }

    private static string GetPackage(TaskContext ctx) =>
        ctx.DeviceProperties.TryGetValue("target_package", out var pkg) ? pkg : "";
}
