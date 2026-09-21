---
trigger: always_on
---

# SC-12: INTELLIGENT DECISION & REAL-WORLD EXECUTION

## Core Principle

> **Do not execute the first possible solution. Understand the task, discover available options, evaluate them using real evidence, select the most appropriate strategy, and execute against real system state.**

Agent phải **ra quyết định trước khi hành động**, thay vì đơn giản thực hiện sequence đầu tiên mà nó nghĩ tới.

Mọi execution phải ưu tiên:

```text
Real State
    ↓
Observe
    ↓
Discover Options
    ↓
Evaluate
    ↓
Select Strategy
    ↓
Execute
    ↓
Verify
```

---

## SC-12.1 — NO FIRST-SOLUTION BIAS

Agent không được mặc định:

```text
"Ta biết cách làm → làm ngay"
```

Mà phải:

```text
Task
 ↓
Understand
 ↓
What options are available?
 ↓
Compare
 ↓
Select
 ↓
Execute
```

Ví dụ Android:

```text
Need to find button
```

Không mặc định:

```text
tap(x=420, y=800)
```

Agent phải ưu tiên discovery:

```text
resource-id
    ↓
content-desc
    ↓
text
    ↓
UI hierarchy
    ↓
accessibility
    ↓
visual detection
    ↓
coordinate fallback
```

Phương án nào có evidence tốt hơn thì được ưu tiên.

---

# SC-12.2 — REAL DATA ONLY

Production execution không được dựa trên fake data để tạo cảm giác hệ thống đang hoạt động.

Không được:

```csharp
var fakeDevice = ...
var fakeBattery = 80;
var fakeOnline = true;
var fakeSuccess = true;
```

để thay thế trạng thái thực tế.

Phải lấy state từ:

```text
ADB
Android Device
UI Hierarchy
Process State
System APIs
Database
Network
File System
Actual Tool Output
```

Ví dụ:

```text
Device connected?
        ↓
ADB devices
        ↓
Actual device state
```

Không:

```text
Connected = true;
```

chỉ vì application muốn coi device là connected.

---

# SC-12.3 — MOCK/FAKE ISOLATION

Mock/Fake data chỉ được phép tồn tại trong:

```text
Unit Test
Integration Test
Simulation
Development Fixture
```

và phải được đánh dấu rõ ràng.

Không được để:

```text
Mock
 ↓
Production
```

một cách vô tình.

Production path phải là:

```text
Real Adapter
 ↓
Real System
 ↓
Real Result
```

---

# SC-12.4 — OPTION DISCOVERY

Khi có nhiều cách thực hiện, Agent phải discover các available strategies.

```csharp
public interface IStrategySelector
{
    Task<StrategySelection> SelectAsync(
        TaskContext context,
        IReadOnlyList<StrategyCandidate> candidates,
        CancellationToken ct);
}
```

Ví dụ:

```text
Strategy A
├── Fast
├── Coordinate based
└── Fragile

Strategy B
├── Medium
├── Resource-ID based
└── Stable

Strategy C
├── Slow
├── Vision based
└── Robust
```

Agent không chọn chỉ vì:

```text
A chạy được
```

Mà phải xem:

```text
Correctness
Reliability
Evidence
Compatibility
Performance
Side Effects
Maintainability
```

---

# SC-12.5 — EVIDENCE-BASED SELECTION

Mọi quyết định quan trọng phải có evidence.

Ví dụ:

```text
Selected Strategy: ResourceIdSelector

Evidence:
- Element has stable resource-id
- Previous executions succeeded: 18/18
- No coordinate dependency
- UI layout changed twice without failure
```

Không:

```text
I think this is better.
```

Evidence phải đến từ:

```text
Current Device State
Execution History
Previous Experiences
Test Results
Playbooks
Tool Capabilities
System Constraints
```

---

# SC-12.6 — SCORE BEFORE SELECT

Khi có nhiều candidate, Agent nên đánh giá theo các tiêu chí cấu hình được.

Ví dụ:

```text
Candidate Strategy
        ↓
Correctness      40%
Reliability     25%
Compatibility   15%
Performance     10%
Maintainability 10%
        ↓
Weighted Score
        ↓
Selected Strategy
```

Không hardcode:

```csharp
if (strategy == "A")
```

Decision policy phải configuration-driven.

Ví dụ:

```json
{
  "StrategySelectionPolicy": {
    "CorrectnessWeight": 0.40,
    "ReliabilityWeight": 0.25,
    "CompatibilityWeight": 0.15,
    "PerformanceWeight": 0.10,
    "MaintainabilityWeight": 0.10
  }
}
```

---

# SC-12.7 — CURRENT STATE OVER ASSUMPTION

Agent phải tin **state quan sát được** hơn assumption.

```text
Assumption
    ↓
Observe
    ↓
Actual State
    ↓
Decision
```

Ví dụ:

Không:

```text
App probably finished loading.
→ tap
```

Mà:

```text
wait_for_ui_stable
 ↓
observe UI
 ↓
element exists
 ↓
tap
```

---

# SC-12.8 — ADAPTIVE EXECUTION

Agent phải thích nghi khi environment thay đổi.

```text
Expected State
      ↓
Observe
      ↓
Same State?
 ├── YES → Continue
 └── NO
       ↓
    Re-evaluate
       ↓
    Select alternative
```

Không được blindly execute:

```text
Step 1
Step 2
Step 3
Step 4
```

nếu state sau Step 2 đã khác expected state.

---

# SC-12.9 — LEAST FRAGILE STRATEGY

Khi nhiều strategy có cùng correctness, ưu tiên strategy ít fragile hơn.

Ưu tiên:

```text
Semantic selector
    >
Resource ID
    >
Accessibility
    >
UI hierarchy
    >
Visual recognition
    >
Coordinate
```

Chỉ là default policy; Agent phải dựa trên evidence thực tế.

Không được biến một heuristic thành absolute rule nếu environment chứng minh ngược lại.

---

# SC-12.10 — REAL VERIFICATION

Command success không đồng nghĩa task success.

```text
Command
 ↓
Tool Result
 ↓
System State
 ↓
Expected State
 ↓
Verification
```

Ví dụ:

```text
adb shell input tap
```

chỉ chứng minh command đã được gửi.

Không chứng minh UI đã thay đổi đúng.

Phải:

```text
tap
 ↓
observe
 ↓
verify expected state
```

---

# SC-12.11 — NO FAKE SUCCESS

Agent tuyệt đối không được biến:

```text
Unknown
```

thành:

```text
Success
```

Nếu không có evidence:

```text
SUCCESS
FAILURE
```

thì trạng thái phải là:

```text
UNKNOWN
```

Ví dụ:

```csharp
public enum ExecutionStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Unknown,
    Cancelled
}
```

`Unknown` không được tự động chuyển thành `Succeeded`.

---

# SC-12.12 — EXPLAINABLE DECISION

Mỗi strategic decision quan trọng phải có decision record.

```csharp
public sealed record DecisionRecord(
    string DecisionId,
    string TaskId,
    IReadOnlyList<string> Candidates,
    string SelectedCandidate,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> RejectedReasons,
    DateTimeOffset CreatedAt
);
```

Ví dụ:

```text
Decision
────────────────────────
Selected: ResourceIdStrategy

Candidates:
- CoordinateStrategy
- TextStrategy
- ResourceIdStrategy
- VisionStrategy

Evidence:
+ resource-id exists
+ stable across previous runs
+ 18/18 successful executions

Rejected:
- Coordinate → fragile
- Text → duplicated text
- Vision → unnecessary overhead
```

Agent phải có khả năng trả lời:

> **"Tại sao mày chọn cách này?"**

---

# SC-12.13 — INTELLIGENCE MUST NOT MEAN RANDOMNESS

"Thông minh" không có nghĩa là mỗi lần làm một cách khác nhau.

Nếu một strategy đã được:

```text
Tested
Verified
Published
```

và phù hợp với current context:

```text
Reuse verified strategy
```

Không cần reinvent.

```text
Known Good Path
       ↓
Reuse
```

Chỉ re-plan khi:

```text
Precondition changed
OR
Environment changed
OR
Verification failed
OR
Better evidence exists
```

---

# SC-12.14 — LEARN FROM REAL OUTCOMES

Agent phải dùng execution thực tế để cải thiện decision-making.

```text
Decision
 ↓
Execution
 ↓
Outcome
 ↓
Experience
 ↓
Reflection
 ↓
Lesson
 ↓
Future Decision
```

Ví dụ:

```text
Strategy A
→ succeeded 20/20

Strategy B
→ succeeded 14/20

Future equivalent task
→ A receives stronger evidence
```

Nhưng historical success không được tự động biến thành guarantee.

Current state vẫn phải được kiểm tra.

---

# SC-12.15 — REALITY OVER SIMULATION

Production Agent phải ưu tiên:

```text
REAL DEVICE
REAL STATE
REAL TOOL OUTPUT
REAL EXECUTION
REAL VERIFICATION
```

Không được tạo illusion rằng hệ thống đã hoàn thành task bằng:

```text
fake response
fake device state
fake database record
fake screenshot
fake success flag
fake execution result
```

Nếu hệ thống chưa thể thực hiện thật:

```text
NOT_SUPPORTED
```

hoặc:

```text
UNKNOWN
```

phải được trả về thay vì giả lập thành công.

---

# SC-12.16 — DECISION QUALITY GATE

Trước execution, Agent phải tự kiểm tra:

* [ ] Task đã được hiểu?
* [ ] Current state đã được observe?
* [ ] Available strategies đã được discover?
* [ ] Có verified playbook phù hợp không?
* [ ] Candidate strategies đã được đánh giá?
* [ ] Strategy được chọn có evidence?
* [ ] Có fallback?
* [ ] Success criteria rõ ràng?
* [ ] Verification method tồn tại?
* [ ] Có fake/mock data trong production path không?

Nếu thiếu critical information:

```text
STOP
 ↓
OBSERVE / DISCOVER
```

Không được đoán.

---

# SC-12.17 — CORE INVARIANT

Hệ thống phải tuân thủ:

```text
REAL STATE
    ↓
UNDERSTAND
    ↓
DISCOVER
    ↓
COMPARE
    ↓
SELECT
    ↓
EXECUTE
    ↓
OBSERVE
    ↓
VERIFY
    ↓
LEARN
```

Không:

```text
GUESS
 ↓
EXECUTE
 ↓
ASSUME SUCCESS
```

### Senior Rule

> **The smartest action is not the fastest action.**
>
> **The smartest action is the action selected from real evidence that best satisfies the task under the current conditions.**
>
> **Never fake reality to make the system look successful.**
