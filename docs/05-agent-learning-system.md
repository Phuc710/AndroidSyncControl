# 🧠 Hệ Thống Agent Tự Học & Thực Thi Thông Minh (Agent Learning System)

Tài liệu này hướng dẫn chi tiết về cấu trúc, nguyên lý vận hành và cách sử dụng module **`AndroidSyncControl.Agent`**. Hệ thống được thiết kế theo tiêu chuẩn kỹ sư cấp cao **SC-11 (Verified Knowledge & Engineering Workflow)** và **SC-12 (Intelligent Decision & Real-World Execution)**.

---

## 1. Triết Lý Cốt Lõi (Core Principles)

> **"Learn once, verify once, reuse many times."**
> **"Do not execute the first possible solution — observe real state, evaluate evidence, verify repeated success."**

Hệ thống Agent giải quyết bài toán tự động hóa Android thông minh:
1. **Không đoán mò (No First-Solution Bias):** Không dùng tọa độ cứng (hardcoded coordinates). Ưu tiên selector theo ngữ nghĩa -> Resource ID -> Accessibility -> Visual detection -> Coordinate fallback.
2. **Không dữ liệu ảo (Real State Only):** Mọi quyết định và đánh giá đều dựa trên kết quả ADB thực tế từ thiết bị, không giả lập thành công khi chưa verify.
3. **Vòng lặp học tập khép kín:** Khi gặp bài toán mới hoặc quy trình thất bại, hệ thống tự khám phá (Explore), ghi nhận dấu vết thực thi (Execution Trace), mổ xẻ nguyên nhân (Reflection), sinh chiến thuật cải tiến (Optimization), kiểm định qua bộ test (Evaluation) trước khi ban hành làm tri thức chuẩn (Publish).

---

## 2. Kiến Trúc Phân Lớp (Directory Architecture)

Toàn bộ mã nguồn nằm tại thư mục `src/AndroidSyncControl/Agent/`:

```
src/AndroidSyncControl/Agent/
├── Domain/                           # Thực thể nghiệp vụ bất biến (Records & Enums)
│   ├── PlaybookState.cs              # 7 trạng thái vòng đời của Playbook
│   ├── Playbook.cs                   # Schema Playbook chuẩn, versioning bất biến
│   ├── PlaybookEvaluationPolicy.cs   # Tiêu chí và ngưỡng điểm đánh giá
│   ├── Experience.cs                 # Dữ liệu lịch sử thực thi (Append-only, Immutable)
│   ├── ExecutionTrace.cs             # Dấu vết sự kiện thô (Raw Event Stream)
│   ├── ReflectionResult.cs           # Kết quả suy ngẫm và danh sách bài học (Lesson)
│   ├── TestCase.cs                   # Cấu trúc Test Case và SuccessCriterion
│   ├── StandardTestCaseGenerator.cs  # Tự động sinh 7 Test Cases chuẩn SC-11 (TC-01..TC-07)
│   ├── TaskContext.cs                # Ngữ cảnh đầu vào của task (Intent, Tags, Device Props)
│   └── MatchEvidence.cs              # Bằng chứng khớp Playbook (T1, T2, T3 BM25)
│
├── Abstractions/                     # Hợp đồng giao tiếp (Interfaces)
│   ├── IPlaybookMatcher.cs           # Khớp task với sách đã có
│   ├── IPlaybookExecutor.cs          # Thực thi Playbook trên thiết bị
│   ├── IAgentReflector.cs            # Phân tích nguyên nhân thành công/thất bại
│   ├── IPlaybookOptimizer.cs         # Đề xuất cải tiến cho phiên bản tiếp theo
│   ├── IPlaybookEvaluator.cs         # Cổng kiểm định duy nhất cho phép Verified
│   └── IPlaybookRepository.cs        # Lưu trữ và truy vấn Playbook/Experience
│
├── Matching/                         # Cơ chế khớp đa tầng (3-Tier Matcher)
│   ├── Bm25PlaybookMatcher.cs        # Điều phối T1 (Exact) -> T2 (Tags) -> T3 (BM25)
│   └── Bm25Index.cs                  # Engine chỉ mục BM25/TF-IDF thuần C# không dependency
│
├── Learning/                         # Tầng tự học và tiến hóa
│   ├── AgentReflector.cs             # Rút ra bài học từ ExecutionTrace
│   └── PlaybookOptimizer.cs          # Nâng cấp Playbook, chèn verify step, tăng timeout
│
├── Evaluation/                       # Cổng kiểm định chất lượng (SC-11 Quality Gate)
│   └── PlaybookEvaluator.cs          # Chạy Test Suite, tính điểm 4 trọng số
│
├── Execution/                        # Tầng kết nối phần cứng Android
│   ├── ExecutionResult.cs            # Kết quả thực thi thực tế kèm ObservedState
│   ├── AndroidActionEngine.cs        # Dispatch lệnh ADB an toàn theo KR-01
│   └── PlaybookExecutor.cs           # Phiên dịch chiến thuật thành lệnh cụ thể
│
├── Repository/                       # Tầng lưu trữ dữ liệu bền vững
│   └── JsonPlaybookRepository.cs     # Lưu file JSON vào thư mục `agent-data/`
│
└── Orchestration/                    # Nhạc trưởng điều phối (Controller)
    └── AgentOrchestrator.cs          # Điều phối trọn vẹn luồng Match -> Reuse / Learn
```

---

## 3. Sơ Đồ Vận Hành Toàn Diện (Orchestration Workflow)

```text
                           [ USER TASK / TaskContext ]
                                        │
                                        ▼
                             ┌─────────────────────┐
                             │  IPlaybookMatcher   │
                             │  T1: Exact Id       │
                             │  T2: Tags Overlap   │
                             │  T3: BM25 Scoring   │
                             └──────────┬──────────┘
                                        │
                         Confidence >= 0.85 & Preconditions OK?
                                   /         \
                                 YES          NO
                                  │            │
                     ┌────────────┘            └─────────────┐
                     ▼                                       ▼
            [ REUSE PATH ]                           [ EXPLORE PATH ]
                   │                                         │
        Thực thi Playbook có sẵn                     Thực thi Draft thăm dò
                   │                                         │
                   ▼                                         ▼
            Quan sát State                            Ghi nhận Experience
                   │                                         │
                   ▼                                         ▼
            Thành công? ──► NO (Regression!)          IAgentReflector
             /        \            │                         │
           YES         NO          ▼                         ▼
            │           └─► Chuyển sang Learn          Rút ra Lesson
            ▼                                                │
       [ HOÀN THÀNH ]                                        ▼
                                                     IPlaybookOptimizer
                                                             │
                                                             ▼
                                                    Candidate Playbook (vN+1)
                                                             │
                                                             ▼
                                                     IPlaybookEvaluator
                                                    (Chạy Test Suite 7 cases)
                                                             │
                                                   Đạt chuẩn Quality Gate?
                                                        /         \
                                                      YES          NO
                                                       │            │
                                                       ▼            ▼
                                                  [ PUBLISH ]   [ REJECT ]
                                                  Lưu vào đĩa   Thử lại loop
```

---

## 4. Máy Trạng Thái Của Playbook (State Machine)

Mỗi Playbook được kiểm soát qua enum [`PlaybookState`](file:///c:/Users/Phucx/Desktop/ROOT_Shopee/src/AndroidSyncControl/Agent/Domain/PlaybookState.cs):

```text
Draft ──► Candidate ──► Testing ──► Verified ──► Published
  │                                                  │
  └────────────────────────► Failed ◄────────────────┴──► Deprecated
```

- **Draft:** Sách nháp sinh ra từ luồng thăm dò (Explore).
- **Candidate:** Sách đã được Optimizer gắn chiến thuật cải tiến và bài học kinh nghiệm.
- **Testing:** Đang được đưa vào Evaluator để chạy bộ test case.
- **Verified:** Đã vượt qua kiểm định khắt khe của `IPlaybookEvaluator` (Correctness 100%, Điểm tổng hợp >= 85%).
- **Published:** Đã lưu trữ chính thức vào Repository, sẵn sàng để Matcher tái sử dụng.
- **Deprecated:** Phiên bản cũ bị thay thế khi có phiên bản mới tốt hơn.
- **Failed:** Bị loại bỏ sau nhiều lần optimize không đạt điểm chuẩn.

---

## 5. Hướng Dẫn Sử Dụng Trong Mã Nguồn (Clean Facade API)

### 5.1. Cú Pháp Chuẩn 1 Dòng Lệnh

Agent cung cấp facade `AndroidAgent` tối giản tối đa, tự động thực hiện 9 bước lifecycle bên dưới:

```csharp
using AndroidSyncControl.Agent;
using AndroidSyncControl.Agent.Domain;

// 1. Khởi tạo Agent (hoặc nạp qua DI với IAndroidAgent)
var agent = AndroidAgent.CreateDefault();

// 2. Thực thi tác vụ
var result = await agent.ExecuteAsync(
    new AgentTask
    {
        Intent = "Open Chrome and verify that it is running",
        DeviceSerial = device.Serial,
        Preconditions =
        [
            "device_connected",
            "chrome_installed"
        ],
        SuccessCriteria =
        [
            "chrome_running"
        ]
    },
    cancellationToken);
```

---

### 5.2. Hiển Thị Kết Quả Đầy Đủ Lên Giao Diện (Rich UI Audit Trail)

Đối tượng trả về [`AgentExecutionResult`](file:///c:/Users/Phucx/Desktop/ROOT_Shopee/src/AndroidSyncControl/Agent/Domain/AgentExecutionResult.cs) mang đầy đủ thông tin để UI hiển thị minh bạch:

```csharp
if (result.Success)
{
    // Trường hợp 1: Tái sử dụng Playbook đã kiểm định (Fast path)
    // Path: ReusedVerifiedPlaybook
    // Playbook: open-chrome v3
    // Strategy: ResourceId ➔ UI verification
    // Confidence: 0.94
    // Experience: exp_20260921_0042
    // Verification: PASSED
    
    // Trường hợp 2: Tự học và xuất bản Playbook mới (Learning path)
    // Path: LearnedAndPublished
    // New Playbook: open-chrome v4
    // Decisions: 4 explainable decision records
    // Evaluation: Correctness 1.00, Stability 0.95, Speed 0.88, SideEffect 1.00
    // Status: VERIFIED ➔ PUBLISHED
}
```

---

### 5.3. Trả Lời 5 Câu Hỏi Cốt Tử (SC & KR-08 Self-Explaining Checklist)

Bất kỳ lập trình viên hay Agent nào nhìn vào hệ thống này đều có ngay câu trả lời:

1. **Nó là gì?** 
   - Hệ thống tự học, tự thích nghi và thực thi tự động hóa Android qua ADB an toàn, không phụ thuộc tọa độ ảo.
2. **Tại sao tồn tại?** 
   - Để triệt tiêu hoàn toàn lỗi vỡ layout khi app cập nhật UI và rủi ro bị Shopee Risk Engine gắn cờ đỏ M02/D02/L01 do các thao tác cứng nhắc.
3. **Dùng nó thế nào?** 
   - Chỉ cần gọi `await agent.ExecuteAsync(new AgentTask { ... })`.
4. **Làm sao biết nó hoạt động đúng?** 
   - Vượt qua cổng kiểm định `PlaybookEvaluator` với 7 test cases chuẩn SC-11 (TC-01..TC-07), Correctness đạt 100%, pass rate xUnit 100%.
5. **Tại sao Agent lại chọn cách này?** 
   - Truy vấn danh sách `result.Decisions` chứa `DecisionRecord` ghi rõ: Candidates được so sánh, Candidate được chọn, Bằng chứng thực tế và Lý do từ chối các phương án khác.

---

## 6. Tiêu Chuẩn Đánh Giá Chất Lượng (SC-11 Quality Gate)

Khi đánh giá một Candidate Playbook, [`PlaybookEvaluator`](file:///c:/Users/Phucx/Desktop/ROOT_Shopee/src/AndroidSyncControl/Agent/Evaluation/PlaybookEvaluator.cs) áp dụng ma trận 4 trọng số khắt khe:

$$\text{Total Score} = (0.40 \times \text{Correctness}) + (0.25 \times \text{Stability}) + (0.20 \times \text{SideEffect}) + (0.15 \times \text{Speed})$$

- **Correctness Score (40% — Hard Gate):** Mọi điều kiện trong `SuccessCriteria` phải đạt 100%. Nếu có bất kỳ tiêu chí nào sai, Playbook bị đánh trượt ngay lập tức bất chấp các điểm số khác.
- **Stability Score (25%):** Đo lường tỷ lệ không xảy ra Crash, Unhandled Exception hoặc ADB Timeout.
- **Side Effect Score (20%):** Đảm bảo thao tác không gây tác dụng phụ ngoài ý muốn (không mở nhầm app, không bấm nhầm nút nguy hiểm).
- **Speed Score (15%):** Thời gian hoàn thành nằm trong ngân sách cho phép (mặc định 30 giây).

Một Playbook chỉ được chuyển sang trạng thái **Verified** khi:
1. `CorrectnessScore == 1.0` (100% tiêu chí đạt).
2. `TotalWeightedScore >= 0.85` (Ngưỡng chất lượng tối thiểu 85%).
3. `RegressionCount == 0` (Không làm hỏng các bước đã từng chạy tốt ở bản trước).

---

## 7. Cấu Trúc Lưu Trữ Dữ Liệu (`agent-data/`)

Toàn bộ tri thức học được được lưu trữ tại thư mục `agent-data/` cạnh root dự án:

```
agent-data/
├── playbooks/
│   ├── pb_shopee_bypass_v1.json
│   ├── pb_shopee_bypass_v2.json
│   └── pb_chatgpt_login_v1.json
└── experiences/
    ├── exp_20260921_150820_001.json
    └── exp_20260921_151245_002.json
```

- **Playbook JSON:** Lưu trữ chiến thuật, danh sách action steps, fallback strategy, và lịch sử phiên bản (`ParentVersion`).
- **Experience JSON:** Lưu trữ toàn bộ dấu vết thực tế (sự kiện, thời gian, trạng thái thiết bị). **File này mang tính bất biến (Immutable), không bao giờ bị sửa đổi hay xóa bỏ để phục vụ đối soát và học hỏi.**
