namespace AndroidSyncControl.Agent.Domain;

/// <summary>
/// Tự động sinh bộ Test Case chuẩn SC-11 (TC-01 đến TC-07) từ <see cref="TaskContext"/> và <see cref="SuccessCriterion"/>.
/// <para>
/// Đảm bảo Agent không bao giờ đánh giá qua loa một happy path duy nhất.
/// </para>
/// </summary>
public static class StandardTestCaseGenerator
{
    /// <summary>
    /// Sinh trọn bộ 7 test cases tiêu chuẩn. Nếu <paramref name="criteria"/> rỗng,
    /// tự động suy luận criteria từ intent và tags trong <paramref name="ctx"/>.
    /// </summary>
    public static IReadOnlyList<TestCase> Generate(
        TaskContext ctx,
        IReadOnlyList<SuccessCriterion>? criteria = null)
    {
        var effectiveCriteria = criteria is not null && criteria.Count > 0
            ? criteria
            : InferDefaultCriteria(ctx);

        var cases = new List<TestCase>
        {
            // TC-01: Happy Path tiêu chuẩn
            new("TC-01_NormalFlow", ctx, effectiveCriteria),

            // TC-02: Slow UI / Network Lag (giả lập UI phản hồi chậm 2500ms)
            new("TC-02_SlowUI",
                WithProperty(ctx, "simulated_latency_ms", "2500"),
                effectiveCriteria),

            // TC-03: Missing Element (Kiểm tra fallback của playbook khi UI đổi layout)
            new("TC-03_MissingElement",
                WithProperty(ctx, "simulate_missing_element", "true"),
                effectiveCriteria),

            // TC-04: Transient Error / Retry (Lần 1 fail nhẹ, retry pass)
            new("TC-04_RetryScenario",
                WithProperty(ctx, "simulate_transient_error", "true"),
                effectiveCriteria),

            // TC-05: Device Reconnect (Mất kết nối ADB tạm thời)
            new("TC-05_DeviceReconnect",
                WithProperty(ctx, "simulate_adb_drop", "true"),
                effectiveCriteria),

            // TC-06: Unexpected State (Pop-up/Permission dialog che màn hình)
            new("TC-06_UnexpectedState",
                WithProperty(ctx, "simulate_modal_dialog", "true"),
                effectiveCriteria),

            // TC-07: Idempotency (Chạy lặp 3 lần kiểm tra tính ổn định)
            new("TC-07_RepeatedExecution",
                WithProperty(ctx, "execution_run_count", "3"),
                effectiveCriteria)
        };

        return cases;
    }

    /// <summary>Suy luận tiêu chí thành công mặc định từ Intent và Tags nếu người dùng không truyền.</summary>
    public static IReadOnlyList<SuccessCriterion> InferDefaultCriteria(TaskContext ctx)
    {
        var list = new List<SuccessCriterion>();
        var tags = ctx.ExtractedTags.Select(t => t.ToLowerInvariant()).ToHashSet();

        if (tags.Contains("shopee"))
        {
            list.Add(new SuccessCriterion("app_running", new Dictionary<string, string>
            {
                ["package"] = "com.shopee.vn"
            }));
        }
        else if (tags.Contains("chatgpt") || tags.Contains("openai"))
        {
            list.Add(new SuccessCriterion("app_running", new Dictionary<string, string>
            {
                ["package"] = "com.openai.chatgpt"
            }));
        }
        else if (tags.Contains("chrome"))
        {
            list.Add(new SuccessCriterion("app_running", new Dictionary<string, string>
            {
                ["package"] = "com.android.chrome"
            }));
        }
        else if (tags.Contains("settings"))
        {
            list.Add(new SuccessCriterion("app_running", new Dictionary<string, string>
            {
                ["package"] = "com.android.settings"
            }));
        }
        else
        {
            // Generic criterion: UI element exists
            list.Add(new SuccessCriterion("ui_element_exists", new Dictionary<string, string>
            {
                ["target"] = "main_screen"
            }));
        }

        return list;
    }

    private static TaskContext WithProperty(TaskContext ctx, string key, string value)
    {
        var props = new Dictionary<string, string>(ctx.DeviceProperties)
        {
            [key] = value
        };
        return ctx with { DeviceProperties = props };
    }
}
