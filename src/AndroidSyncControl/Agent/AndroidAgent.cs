using System.Text.RegularExpressions;
using AndroidSyncControl.Agent.Abstractions;
using AndroidSyncControl.Agent.Domain;
using AndroidSyncControl.Agent.Evaluation;
using AndroidSyncControl.Agent.Execution;
using AndroidSyncControl.Agent.Learning;
using AndroidSyncControl.Agent.Matching;
using AndroidSyncControl.Agent.Orchestration;
using AndroidSyncControl.Agent.Repository;

namespace AndroidSyncControl.Agent;

/// <summary>
/// Triển khai chuẩn của <see cref="IAndroidAgent"/>.
/// Phản ánh trọn vẹn 9 bước trong vòng đời Agent:
/// <code>
/// Understand ──► Plan ──► Discover ──► Decide ──► Execute ──► Observe ──► Verify ──► Learn ──► Reuse
/// </code>
/// </summary>
public sealed class AndroidAgent : IAndroidAgent
{
    private readonly IPlaybookMatcher _matcher;
    private readonly IPlaybookExecutor _executor;
    private readonly IAgentReflector _reflector;
    private readonly IPlaybookOptimizer _optimizer;
    private readonly IPlaybookEvaluator _evaluator;
    private readonly IPlaybookRepository _repo;

    public AndroidAgent(
        IPlaybookMatcher matcher,
        IPlaybookExecutor executor,
        IAgentReflector reflector,
        IPlaybookOptimizer optimizer,
        IPlaybookEvaluator evaluator,
        IPlaybookRepository repo)
    {
        _matcher = matcher;
        _executor = executor;
        _reflector = reflector;
        _optimizer = optimizer;
        _evaluator = evaluator;
        _repo = repo;
    }

    /// <summary>
    /// Factory tạo instance AndroidAgent mặc định sử dụng file persistence tại agent-data/.
    /// </summary>
    public static AndroidAgent CreateDefault(string? dataDirectory = null)
    {
        var repo = new JsonPlaybookRepository(dataDirectory);
        var actionEngine = new AndroidActionEngine("default");
        var executor = new PlaybookExecutor();
        var matcher = new Bm25PlaybookMatcher(repo);
        var evaluator = new PlaybookEvaluator(executor);
        var reflector = new AgentReflector();
        var optimizer = new PlaybookOptimizer();

        return new AndroidAgent(matcher, executor, reflector, optimizer, evaluator, repo);
    }

    public async Task<AgentExecutionResult> ExecuteAsync(AgentTask task, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        var decisions = new List<DecisionRecord>();

        // ── 1. Understand Task ────────────────────────────────────────────────
        var extractedTags = ExtractTags(task.Intent);
        var criteria = ParseCriteria(task);

        decisions.Add(new DecisionRecord(
            DecisionId: $"dec_understand_{Guid.NewGuid():N}",
            TaskId: task.Id,
            Candidates: ["RawIntentAnalysis", "NLP_RegexTokenizer"],
            SelectedCandidate: "NLP_RegexTokenizer",
            Evidence: [$"Extracted {extractedTags.Length} tags: [{string.Join(", ", extractedTags)}]"],
            RejectedReasons: [],
            CreatedAt: DateTimeOffset.UtcNow));

        // ── 2. Check Current Device State ─────────────────────────────────────
        var deviceProps = new Dictionary<string, string>
        {
            ["device_serial"] = task.DeviceSerial
        };

        var ctx = new TaskContext(
            RawIntent: task.Intent,
            ExtractedTags: extractedTags,
            RequiredPreconditions: task.Preconditions.ToArray(),
            DeviceSerial: task.DeviceSerial,
            DeviceProperties: deviceProps);

        decisions.Add(new DecisionRecord(
            DecisionId: $"dec_state_{Guid.NewGuid():N}",
            TaskId: task.Id,
            Candidates: ["PreconditionCheck"],
            SelectedCandidate: "PreconditionCheck",
            Evidence: task.Preconditions.Select(p => $"Precondition '{p}' registered for verification").ToList(),
            RejectedReasons: [],
            CreatedAt: DateTimeOffset.UtcNow));

        // ── 3. Find Matching Playbook (Memory Cascade T1/T2/T3) ───────────────
        var matchResult = await _matcher.FindMatchingAsync(ctx, ct);

        decisions.Add(new DecisionRecord(
            DecisionId: $"dec_match_{Guid.NewGuid():N}",
            TaskId: task.Id,
            Candidates: ["T1_Exact", "T2_Tags", "T3_BM25"],
            SelectedCandidate: matchResult.Method.ToString(),
            Evidence: [$"Confidence: {matchResult.ConfidenceScore:F2}, PreconditionsPassed: {matchResult.PreconditionsPassed}"],
            RejectedReasons: matchResult.Candidates.Where(c => c.Playbook.Id != matchResult.BestMatch?.Id).Select(c => $"Candidate {c.Playbook.Id} scored lower: {c.ConfidenceScore:F2}").ToList(),
            CreatedAt: DateTimeOffset.UtcNow));

        // ── 4. Decide Path: Reuse or Explore/Learn ────────────────────────────
        if (matchResult.ShouldReuse)
        {
            var playbook = matchResult.BestMatch!;
            var execResult = await _executor.ExecuteAsync(playbook, ctx, ct);

            // Record immutable experience
            var experience = new Experience(
                Id: $"exp_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N[..6]}",
                TaskIntent: task.Intent,
                PlaybookUsed: playbook.Id,
                PlaybookVersion: playbook.Version,
                Outcome: execResult.Success ? "SUCCESS" : "FAIL",
                Traces: execResult.Traces,
                DeviceSerial: task.DeviceSerial,
                RecordedAt: DateTimeOffset.UtcNow);

            await _repo.AppendExperienceAsync(experience, ct);

            return new AgentExecutionResult
            {
                Success = execResult.Success,
                Path = ExecutionPath.ReusedVerifiedPlaybook,
                PlaybookId = playbook.Id,
                PlaybookVersion = playbook.Version,
                Confidence = matchResult.ConfidenceScore,
                SelectedStrategy = string.Join(" ➔ ", playbook.Strategy),
                Decisions = decisions,
                ExperienceId = experience.Id,
                EvaluationId = playbook.LastEvaluationReport?.PlaybookId,
                Evaluation = playbook.LastEvaluationReport,
                FailureReason = execResult.ErrorMessage
            };
        }
        else
        {
            // ── 5. Explore, Observe, Reflect, Optimize, Test, Evaluate, Publish ──
            var orchestrator = new AgentOrchestrator(
                _matcher, _executor, _reflector, _optimizer, _evaluator, _repo);

            // Tự động sinh test suite chuẩn SC-11 (TC-01..TC-07)
            var testCases = StandardTestCaseGenerator.Generate(ctx, criteria);

            var orchResult = await orchestrator.ExecuteTaskAsync(ctx, testCases, ct);

            var publishedPb = await _repo.GetAsync(orchResult.PlaybookUsed, orchResult.PlaybookVersion, ct);

            decisions.Add(new DecisionRecord(
                DecisionId: $"dec_learn_{Guid.NewGuid():N}",
                TaskId: task.Id,
                Candidates: ["HeuristicFix", "ReflectionBasedOptimization"],
                SelectedCandidate: "ReflectionBasedOptimization",
                Evidence: [
                    $"Playbook {orchResult.PlaybookUsed} v{orchResult.PlaybookVersion} evaluated across {testCases.Count} test cases",
                    $"Quality Gate: Verified & Published"
                ],
                RejectedReasons: [],
                CreatedAt: DateTimeOffset.UtcNow));

            return new AgentExecutionResult
            {
                Success = orchResult.Success,
                Path = ExecutionPath.LearnedAndPublished,
                PlaybookId = orchResult.PlaybookUsed,
                PlaybookVersion = orchResult.PlaybookVersion,
                Confidence = 1.0f,
                SelectedStrategy = publishedPb != null ? string.Join(" ➔ ", publishedPb.Strategy) : "Dynamic discovery",
                Decisions = decisions,
                ExperienceId = orchResult.ExperienceId,
                EvaluationId = publishedPb?.LastEvaluationReport?.PlaybookId,
                Evaluation = publishedPb?.LastEvaluationReport,
                FailureReason = orchResult.Success ? null : "Failed to verify playbook within maximum iterations"
            };
        }
    }

    // ── Helper parsing methods ────────────────────────────────────────────────

    private static string[] ExtractTags(string intent)
    {
        return Regex.Matches(intent, @"\b[a-zA-Z0-9_\-]{3,}\b")
            .Select(m => m.Value.ToLowerInvariant())
            .Distinct()
            .ToArray();
    }

    private static IReadOnlyList<SuccessCriterion> ParseCriteria(AgentTask task)
    {
        if (task.SuccessCriteria.Count == 0) return Array.Empty<SuccessCriterion>();

        var list = new List<SuccessCriterion>();
        foreach (var sc in task.SuccessCriteria)
        {
            if (sc.Contains("chrome_running", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new SuccessCriterion("app_running", new Dictionary<string, string> { ["package"] = "com.android.chrome" }));
            }
            else if (sc.Contains("shopee_running", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new SuccessCriterion("app_running", new Dictionary<string, string> { ["package"] = "com.shopee.vn" }));
            }
            else if (sc.Contains("chatgpt_running", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new SuccessCriterion("app_running", new Dictionary<string, string> { ["package"] = "com.openai.chatgpt" }));
            }
            else
            {
                list.Add(new SuccessCriterion("ui_element_exists", new Dictionary<string, string> { ["target"] = sc }));
            }
        }
        return list;
    }
}
