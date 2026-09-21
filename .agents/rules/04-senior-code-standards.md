---
trigger: always_on
---

## SC-11: VERIFIED KNOWLEDGE & ENGINEERING WORKFLOW

### Mục tiêu

Mọi capability mới của Agent phải đi qua một quy trình có kiểm soát:

```text
TASK
 ↓
UNDERSTAND
 ↓
PLAN
 ↓
EXECUTE
 ↓
OBSERVE
 ↓
REFLECT
 ↓
OPTIMIZE
 ↓
REVIEW
 ↓
TEST
 ↓
EVALUATE
 ↓
VERIFY
 ↓
PUBLISH
 ↓
REUSE
 ↓
REGRESSION MONITORING
```

Không được bỏ qua các quality gate chỉ vì một execution đã thành công.

---

# SC-11.1 — TASK INTAKE

Mọi yêu cầu mới phải bắt đầu bằng một `AgentTask`.

Task phải xác định:

```csharp
public sealed record AgentTask(
    string Id,
    string Intent,
    string? TaskType,
    IReadOnlyList<string> Preconditions,
    IReadOnlyList<string> SuccessCriteria,
    string DeviceSerial,
    DateTimeOffset CreatedAt
);
```

### Task phải trả lời được

* Agent cần làm gì?
* Trên device nào?
* Điều kiện đầu vào là gì?
* Thành công nghĩa là gì?
* Có giới hạn/ràng buộc nào?
* Có Playbook cũ phù hợp không?

Không được bắt đầu execution nếu task chưa có success criteria.

---

# SC-11.2 — UNDERSTAND

Agent phải phân tích task trước khi hành động.

```text
User Intent
    ↓
Task Understanding
    ↓
Extract:
├── Intent
├── Entities
├── Preconditions
├── Constraints
└── Success Criteria
```

Agent phải kiểm tra Memory:

```text
Task
 ↓
PlaybookMatcher
 ↓
┌─────────────────────────────┐
│ Exact Match                 │
│ Tag Match                   │
│ BM25 / Semantic Match       │
└─────────────────────────────┘
```

Nếu tìm được Playbook đủ confidence:

```text
USE VERIFIED PLAYBOOK
```

Nếu không:

```text
ENTER LEARNING MODE
```

---

# SC-11.3 — PLAN

Agent phải tạo execution plan trước khi thực thi task mới.

```csharp
public sealed record ExecutionPlan(
    string TaskId,
    IReadOnlyList<PlannedStep> Steps,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Fallbacks
);
```

Plan phải mô tả:

```text
Goal
 ↓
Step 1
 ↓
Step 2
 ↓
Step 3
 ↓
Verification
```

Không được để Agent thực hiện một chuỗi hành động phức tạp mà không có execution plan.

---

# SC-11.4 — EXECUTE

Execution phải thông qua `IPlaybookExecutor` hoặc `IAgentExecutor`.

Mọi step phải tạo trace:

```csharp
public sealed record StepExecution(
    string StepId,
    string Action,
    object? Input,
    object? Output,
    bool Success,
    string? Error,
    long DurationMs,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt
);
```

Execution phải:

* hỗ trợ `CancellationToken`
* ghi execution trace
* không mutate Playbook đang chạy
* không ghi đè Experience cũ
* có timeout
* có retry policy khi phù hợp
* kiểm tra preconditions
* kiểm tra success criteria

---

# SC-11.5 — OBSERVE

Agent không được giả định rằng action thành công chỉ vì command không báo lỗi.

Ví dụ:

```text
tap() → command success
```

không đồng nghĩa:

```text
UI action → business success
```

Phải observe state:

```text
Action
 ↓
Observe
 ↓
UI / Device / Process State
 ↓
Verify Expected Result
```

Mọi automation quan trọng phải có explicit verification step.

---

# SC-11.6 — EXPERIENCE CAPTURE

Sau mỗi execution phải tạo `Experience`.

```text
Execution
 ↓
ExecutionTrace
 ↓
Experience
```

Experience phải giữ:

```text
Task
Playbook Used
Version
Actions
Inputs
Outputs
Errors
Timing
Retries
Device State
Outcome
Causal Evidence
```

Experience là **immutable historical evidence**.

Không sửa Experience để làm cho execution cũ trở thành success.

---

# SC-11.7 — REFLECTION

Sau execution, `IAgentReflector` phân tích Experience.

```csharp
public interface IAgentReflector
{
    Task<ReflectionResult> ReflectAsync(
        Experience experience,
        CancellationToken ct);
}
```

Reflection phải xác định:

```text
What happened?
Why did it happen?
What failed?
What worked?
What should change?
What evidence supports the conclusion?
```

Output:

```csharp
public sealed record Lesson(
    string Id,
    string Statement,
    string Evidence,
    float Confidence,
    LessonStatus Status
);
```

Một Experience đơn lẻ không được tự động trở thành permanent rule.

---

# SC-11.8 — OPTIMIZATION

`IPlaybookOptimizer` nhận:

```text
Current Strategy
+
Lessons
+
Execution Evidence
+
Previous Failures
```

và tạo Candidate Playbook mới.

```text
vN
 ↓
Lessons
 ↓
Optimizer
 ↓
vN+1 Candidate
```

Không modify trực tiếp `vN`.

Ví dụ:

```text
v1 → failed
v2 → improved
v3 → improved
v4 → candidate
```

Mỗi version phải giữ parent:

```csharp
ParentVersion
```

---

# SC-11.9 — ENGINEERING REVIEW

Mọi Candidate Playbook phải qua review trước khi verification.

Review phải kiểm tra:

### Correctness

* [ ] Steps đúng mục tiêu?
* [ ] Success criteria đầy đủ?
* [ ] Không có step thừa?
* [ ] Không phụ thuộc state ngầm?

### Reliability

* [ ] Có timeout?
* [ ] Có retry hợp lý?
* [ ] Có fallback?
* [ ] Có xử lý device disconnect?
* [ ] Có xử lý UI chưa ready?

### Maintainability

* [ ] Không hardcode magic values?
* [ ] Config-driven?
* [ ] Không hardcode coordinates nếu có selector tốt hơn?
* [ ] Version rõ ràng?
* [ ] Có logging?

### Safety

* [ ] Preconditions rõ ràng?
* [ ] Side effects được xác định?
* [ ] Không thực hiện action ngoài task scope?

### Testability

* [ ] Có thể chạy test độc lập?
* [ ] Có deterministic success criteria?
* [ ] Có mock/fake execution path?

Candidate không đạt review:

```text
Candidate
 ↓
REJECT
 ↓
Reflection
 ↓
Optimize
 ↓
Candidate vN+1
```

---

# SC-11.10 — TESTING

Candidate phải được test trước khi trở thành Verified.

```text
Candidate
 ↓
Test Case Set
 ↓
Execute
 ↓
Collect Evaluation Report
```

Test case nên bao gồm:

```text
TC-01 Normal flow
TC-02 Slow UI
TC-03 Missing element
TC-04 Retry scenario
TC-05 Device reconnect
TC-06 Unexpected state
TC-07 Repeated execution
```

Không được chỉ test một happy path.

---

# SC-11.11 — EVALUATION

`IPlaybookEvaluator` quyết định chất lượng Candidate.

```csharp
public interface IPlaybookEvaluator
{
    Task<EvaluationReport> EvaluateAsync(
        Playbook candidate,
        IReadOnlyList<TestCase> testCases,
        CancellationToken ct);
}
```

Evaluation gồm:

```text
Correctness
Stability
Performance
Side Effects
Test Pass Rate
Regression Count
```

Correctness là hard gate.

```text
Weighted Score >= threshold
AND
Correctness >= minimum
AND
Required Tests PASS
AND
No blocking regression
```

mới được:

```text
VERIFIED
```

---

# SC-11.12 — VERIFICATION GATE

Chỉ `IPlaybookEvaluator` được quyết định Candidate có đủ điều kiện Verified hay không.

Không cho:

```text
Executor → Published
Reflector → Published
Optimizer → Published
```

Flow bắt buộc:

```text
Candidate
 ↓
Review
 ↓
Testing
 ↓
Evaluation
 ↓
Verified
```

---

# SC-11.13 — PUBLISH

Sau khi Verified mới được publish.

```text
Verified
 ↓
Publish
 ↓
Tool Registry
```

Published Playbook trở thành reusable capability:

```text
Playbook
 ↓
Workflow Tool
 ↓
Tool Registry
 ↓
Agent
```

Tool phải reference đúng Playbook version.

```csharp
PlaybookId
Version
```

Không reference kiểu:

```text
latest
```

vì sẽ phá reproducibility.

---

# SC-11.14 — REUSE

Khi task tương tự xuất hiện:

```text
Task
 ↓
Matcher
 ↓
Published / Verified Playbook
 ↓
Precondition Check
 ↓
Execute
 ↓
Verify
```

Agent không cần tự suy luận lại toàn bộ procedure.

Đây là mục tiêu của Learning System:

> **Learn once, verify once, reuse many times.**

Tuy nhiên mỗi lần reuse vẫn phải ghi Experience mới.

```text
Playbook v4
   ↓
Run #101
   ↓
Experience #101
```

Knowledge không ngừng được quan sát.

---

# SC-11.15 — REGRESSION DETECTION

Nếu Published Playbook bắt đầu fail:

```text
Published v4
     ↓
Execution
     ↓
FAIL
     ↓
Regression Detection
```

Không overwrite v4.

Tạo:

```text
v5 → Learning
```

và giữ:

```text
v4 → Published
```

cho fallback.

Nếu v5 được verify:

```text
v4 → Deprecated
v5 → Published
```

---

# SC-11.16 — ROLLBACK

Nếu Published version mới gây regression:

```text
v5 Published
    ↓
Regression
    ↓
Rollback
    ↓
v4 Published
```

Rollback phải giữ nguyên lịch sử.

Không delete v5.

```text
v4 → Published
v5 → Deprecated / Failed
```

---

# SC-11.17 — KNOWLEDGE INTEGRITY

Phải phân biệt rõ:

```text
Experience
    = Evidence

Lesson
    = Hypothesis

Candidate Playbook
    = Proposed Strategy

Verified Playbook
    = Tested Knowledge

Published Playbook
    = Reusable Knowledge
```

Không được nhảy tầng:

```text
Experience → Published       ❌
Experience → Verified        ❌
Lesson → Published           ❌
Candidate → Published        ❌
```

Bắt buộc:

```text
Evidence
 ↓
Reflection
 ↓
Optimization
 ↓
Review
 ↓
Testing
 ↓
Evaluation
 ↓
Verification
 ↓
Publication
```

---

# SC-11.18 — REQUIRED ARTIFACTS

Mỗi learning cycle phải tạo được:

```text
Task
 ├── ExecutionPlan
 ├── ExecutionTrace
 ├── Experience
 ├── ReflectionResult
 ├── Lessons
 ├── CandidatePlaybook
 ├── ReviewResult
 ├── TestResults
 ├── EvaluationReport
 └── FinalPlaybook
```

Không được chỉ lưu:

```text
v3.json
```

mà không biết:

> v3 được tạo từ đâu, vì sao thay đổi, test thế nào và tại sao được approve.

---

# SC-11.19 — REVIEW CHECKLIST

Trước khi merge code:

* [ ] Task contract rõ ràng?
* [ ] Success criteria rõ ràng?
* [ ] Execution plan tồn tại?
* [ ] Experience được ghi immutable?
* [ ] Causal evidence được lưu?
* [ ] Reflection tách khỏi execution?
* [ ] Lessons có evidence?
* [ ] Candidate có parent version?
* [ ] Candidate đã qua engineering review?
* [ ] Test cases tồn tại?
* [ ] Evaluation report tồn tại?
* [ ] Correctness hard gate?
* [ ] Playbook chỉ Verified sau evaluation?
* [ ] Published version immutable?
* [ ] Reuse tạo Experience mới?
* [ ] Regression có detection?
* [ ] Có rollback?
* [ ] Không overwrite historical versions?

---

## SC-11 Core Principle

> **Do not remember what happened. Learn why it happened.**
>
> **Do not trust what worked once. Verify what works repeatedly.**
>
> **Do not overwrite history. Create a better version.**
>
> **Do not make the Agent think less by removing reasoning. Make it think less because it has already learned a verified procedure.**
