# Tasks: ChatClient 工具绑定与对话调用

**Input**: Design documents from `/specs/010-chatclient-tool-binding/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

**Tests**: Required — Constitution Principle II (TDD) mandates Red-Green-Refactor for all feature code.

**Organization**: Tasks grouped by user story. US2 (backend pipeline) must complete before US3 (frontend visualization). US1 (tool picker) and US4 (detail page) have their own phases.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- **[Story]**: Which user story (US1, US2, US3, US4) — present in story phases only

---

## Phase 1: Setup

**Purpose**: No new projects needed. Minor shared infrastructure setup.

- [ ] T001 Verify `Microsoft.Extensions.AI` is transitively available by adding `using Microsoft.Extensions.AI` to a scratch file in `Backend/CoreSRE.Infrastructure/` and confirming it compiles — no new NuGet packages needed

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Repository extensions, interfaces, and AIFunction subclasses that ALL user stories depend on. Must complete before any story phase.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Tests (Red Phase)

- [ ] T002 [P] Write `ToolFunctionFactoryTests` in `Backend/CoreSRE.Infrastructure.Tests/Services/ToolFunctionFactoryTests.cs` — test `CreateFunctionsAsync` with: (a) empty toolRefs returns empty list, (b) REST API tool ref resolves to AIFunction with correct Name/Description/JsonSchema, (c) MCP tool ref resolves to AIFunction with correct ToolName/InputSchema, (d) mixed REST+MCP refs returns combined list, (e) deleted/unknown ref IDs are skipped with no exception, (f) null ToolSchema.InputSchema produces AIFunction with null JsonSchema. Use Moq for `IToolRegistrationRepository`, `IMcpToolItemRepository`, `IToolInvokerFactory`.
- [ ] T003 [P] Write `ToolRegistrationAIFunctionTests` in `Backend/CoreSRE.Infrastructure.Tests/Services/ToolRegistrationAIFunctionTests.cs` — test: (a) Name returns ToolRegistration.Name, (b) Description returns ToolRegistration.Description, (c) JsonSchema parses ToolSchemaVO.InputSchema string to JsonElement, (d) InvokeCoreAsync delegates to IToolInvoker.InvokeAsync with correct parameters and returns serialized result, (e) InvokeCoreAsync returns error string when IToolInvoker returns Success=false.
- [ ] T004 [P] Write `McpToolAIFunctionTests` in `Backend/CoreSRE.Infrastructure.Tests/Services/McpToolAIFunctionTests.cs` — test: (a) Name returns McpToolItem.ToolName, (b) JsonSchema returns McpToolItem.InputSchema directly, (c) InvokeCoreAsync delegates to IToolInvoker.InvokeAsync with parentTool and mcpToolName, (d) InvokeCoreAsync passes correct parameters dictionary.

### Interfaces

- [ ] T005 [P] Add `GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken)` method to `IToolRegistrationRepository` in `Backend/CoreSRE.Domain/Interfaces/IToolRegistrationRepository.cs`
- [ ] T006 [P] Add `GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken)` method to `IMcpToolItemRepository` in `Backend/CoreSRE.Domain/Interfaces/IMcpToolItemRepository.cs`
- [ ] T007 Create `IToolFunctionFactory` interface in `Backend/CoreSRE.Application/Interfaces/IToolFunctionFactory.cs` with `CreateFunctionsAsync(IReadOnlyList<Guid> toolRefs, CancellationToken)` returning `Task<IReadOnlyList<AIFunction>>`

### Implementation (Green Phase)

- [ ] T008 [P] Implement `GetByIdsAsync` in `ToolRegistrationRepository` in `Backend/CoreSRE.Infrastructure/Persistence/Repositories/ToolRegistrationRepository.cs` using EF Core `.Where(x => ids.Contains(x.Id)).Include(x => x.McpToolItems)`
- [ ] T009 [P] Implement `GetByIdsAsync` in `McpToolItemRepository` in `Backend/CoreSRE.Infrastructure/Persistence/Repositories/McpToolItemRepository.cs` using EF Core `.Where(x => ids.Contains(x.Id)).Include(x => x.ToolRegistration)`
- [ ] T010 Create `ToolRegistrationAIFunction` class in `Backend/CoreSRE.Infrastructure/Services/ToolRegistrationAIFunction.cs` — subclass `AIFunction`, override `Name`, `Description`, `JsonSchema` (parse `ToolSchemaVO.InputSchema` string → JsonElement), `InvokeCoreAsync` (deserialize args to `IDictionary<string, object?>`, call `IToolInvoker.InvokeAsync(tool, null, params)`, serialize `ToolInvocationResultDto` to JSON string result)
- [ ] T011 Create `McpToolAIFunction` class in `Backend/CoreSRE.Infrastructure/Services/McpToolAIFunction.cs` — subclass `AIFunction`, override `Name` (McpToolItem.ToolName), `Description`, `JsonSchema` (use McpToolItem.InputSchema directly), `InvokeCoreAsync` (call `IToolInvoker.InvokeAsync(parentTool, mcpToolName, params)`)
- [ ] T012 Create `ToolFunctionFactory` class in `Backend/CoreSRE.Infrastructure/Services/ToolFunctionFactory.cs` — implement `IToolFunctionFactory`, inject `IToolRegistrationRepository`, `IMcpToolItemRepository`, `IToolInvokerFactory`, `ILogger<ToolFunctionFactory>`. Flow: query both repos with `GetByIdsAsync(toolRefs)` in parallel, create `ToolRegistrationAIFunction` for each REST API match, create `McpToolAIFunction` for each MCP match (load parent ToolRegistration via navigation property), log warnings for unmatched IDs, return combined list.
- [ ] T013 Register `IToolFunctionFactory` → `ToolFunctionFactory` as scoped service in DI container in `Backend/CoreSRE/Program.cs` or `Backend/CoreSRE.Infrastructure/DependencyInjection.cs` (whichever holds service registrations)
- [ ] T014 Run all tests in `Backend/CoreSRE.Infrastructure.Tests/` — verify T002, T003, T004 pass (Green phase)

**Checkpoint**: Foundation ready — `IToolFunctionFactory` is operational, repository batch fetch works, AIFunction subclasses convert tools correctly.

---

## Phase 3: User Story 2 — LLM 对话中的工具自动调用 (Priority: P1) 🎯 MVP

**Goal**: ChatClient agents with bound tools automatically invoke them during LLM conversations via FunctionInvokingChatClient pipeline. Unbound agents behave unchanged.

**Independent Test**: Create agent with ToolRefs → send message requiring tool → LLM calls tool → returns tool-informed response.

### Tests (Red Phase)

- [ ] T015 [US2] Write `AgentResolverServiceTests` in `Backend/CoreSRE.Infrastructure.Tests/Services/AgentResolverServiceTests.cs` — test `ResolveChatClientAgent`: (a) agent with empty ToolRefs returns IChatClient WITHOUT FunctionInvokingChatClient wrapper (backward compat), (b) agent with ToolRefs calls IToolFunctionFactory.CreateFunctionsAsync and returns IChatClient wrapped with FunctionInvokingChatClient, (c) resolved IChatClient pipeline includes tools in ChatOptions when ToolRefs are present. Mock `IAgentRegistrationRepository`, `ILlmProviderRepository`, `IHttpClientFactory`, `IToolFunctionFactory`.

### Implementation (Green Phase)

- [ ] T016 [US2] Modify `AgentResolverService` constructor in `Backend/CoreSRE.Infrastructure/Services/AgentResolverService.cs` — add `IToolFunctionFactory` dependency
- [ ] T017 [US2] Modify `ResolveChatClientAgent` in `Backend/CoreSRE.Infrastructure/Services/AgentResolverService.cs` — after creating base `IChatClient`, if `LlmConfig.ToolRefs` is non-empty: call `IToolFunctionFactory.CreateFunctionsAsync(toolRefs)`, wrap `IChatClient` with `.AsBuilder().UseFunctionInvocation(opts => { opts.MaximumIterationsPerRequest = 10; }).Build()`, store AIFunction list to pass via ChatOptions later
- [ ] T018 [US2] Modify `AgentResolverService.ResolveChatClientAgent` return — ensure `AIAgent` is created with `ChatOptions.Tools` populated from resolved AIFunction list so the FunctionInvokingChatClient has tool definitions. Alternatively, attach tools to agent options/metadata so `AgentChatEndpoints` can retrieve them at streaming time.
- [ ] T019 [US2] Run tests `AgentResolverServiceTests` — verify T015 passes (Green phase)

**Checkpoint**: Backend pipeline wired — ChatClient agents with ToolRefs get FunctionInvokingChatClient wrapping. Tool calls execute automatically during `GetStreamingResponseAsync`. No frontend visibility yet.

---

## Phase 4: User Story 3 — AG-UI 工具调用过程可视化 (Priority: P1)

**Goal**: Tool calls are visible in the chat UI via SSE events (backend) and tool call cards (frontend). Users see real-time tool execution progress.

**Independent Test**: Chat with tool-bound agent → observe TOOL_CALL_START/ARGS/END SSE events in browser devtools → see tool call cards in chat flow.

**Depends on**: Phase 3 (US2) — the FunctionInvokingChatClient pipeline must be wired for tool calls to appear in the streaming output.

### Backend — AG-UI SSE Events

- [ ] T020 [US3] Modify `HandleChatClientStreamAsync` in `Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs` — extend the streaming loop to detect `FunctionCallContent` in `update.Contents`: when found, emit `TOOL_CALL_START` SSE event with `toolCallId` (from `FunctionCallContent.CallId`), `toolCallName` (from `FunctionCallContent.Name`), `parentMessageId` (current assistant messageId), then emit `TOOL_CALL_ARGS` with serialized arguments JSON as `delta`
- [ ] T021 [US3] Modify `HandleChatClientStreamAsync` in `Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs` — detect `FunctionResultContent` in `update.Contents`: when found, emit `TOOL_CALL_END` SSE event with matching `toolCallId` (from `FunctionResultContent.CallId`)
- [ ] T022 [US3] Add SSE helper methods in `Backend/CoreSRE/Endpoints/AgentChatEndpoints.cs` — `WriteToolCallStartAsync`, `WriteToolCallArgsAsync`, `WriteToolCallEndAsync` following the same pattern as existing `WriteTextMessageStartAsync` etc. JSON format per `contracts/agui-tool-events.md`
- [ ] T023 [US3] Test AG-UI event ordering manually using `Backend/CoreSRE/CoreSRE.http` — add a test request to POST `/api/chat/stream` with a tool-bound agent, verify SSE output contains TOOL_CALL_START → TOOL_CALL_ARGS → TOOL_CALL_END → TEXT_MESSAGE_CONTENT sequence

### Frontend — Types & SSE Parsing

- [ ] T024 [P] [US3] Add `ToolCall` interface and extend `ChatMessage` with optional `toolCalls?: ToolCall[]` field in `Frontend/src/types/chat.ts` — `ToolCall` has `toolCallId: string`, `toolName: string`, `status: "calling" | "completed" | "failed"`, `args?: string`, `result?: string`
- [ ] T025 [US3] Modify `use-agent-chat.ts` in `Frontend/src/hooks/use-agent-chat.ts` — add event handlers for `TOOL_CALL_START` (create new ToolCall entry with status "calling" on current assistant message), `TOOL_CALL_ARGS` (update ToolCall args field), `TOOL_CALL_END` (update ToolCall status to "completed") in the SSE event parsing switch/if block

### Frontend — Tool Call Card Component

- [ ] T026 [US3] Create `ToolCallCard.tsx` in `Frontend/src/components/chat/ToolCallCard.tsx` — collapsible card component accepting `ToolCall` prop: shows tool name header with status badge (blue "Calling..." with Loader2 spinner / green "Completed" / red "Failed"), collapsible sections for arguments (JSON formatted, default collapsed) and result (JSON formatted, default collapsed on success, expanded on failure). Use shadcn `Card`, `Collapsible`, `Badge` components.
- [ ] T027 [US3] Modify `MessageBubble.tsx` in `Frontend/src/components/chat/MessageBubble.tsx` — for assistant messages with `toolCalls` array, render `ToolCallCard` for each tool call before the text content. If message has tool calls but no text content yet (still streaming), show only tool call cards.

**Checkpoint**: Full tool call visualization working — users see tool name, args, status, and result in the chat flow.

---

## Phase 5: User Story 1 — 工具绑定配置（可视化工具选择器）(Priority: P1)

**Goal**: Admin can bind tools to ChatClient agents via a searchable multi-select picker, replacing the manual GUID input.

**Independent Test**: Edit a ChatClient agent → open tool picker → search and select tools → save → refresh → confirm tools are persisted.

### Backend — Available Functions Endpoint

- [ ] T028 [US1] Create `GetAvailableFunctionsQuery` and handler in `Backend/CoreSRE.Application/Tools/Queries/GetAvailableFunctions/` — query both `IToolRegistrationRepository` (filter RestApi, Active status) and `IMcpToolItemRepository` (all items from Active MCP servers), map to `BindableToolDto` list with fields: `Id`, `Name`, `Description`, `ToolType` ("RestApi"/"McpTool"), `ParentName` (null for RestApi, parent ToolRegistration.Name for McpTool), `Status`. Support optional `search` and `status` query parameters.
- [ ] T029 [P] [US1] Create `BindableToolDto` in `Backend/CoreSRE.Application/Tools/DTOs/BindableToolDto.cs` with properties: `Guid Id`, `string Name`, `string? Description`, `string ToolType`, `string? ParentName`, `string Status`
- [ ] T030 [US1] Add `GET /api/tools/available-functions` endpoint in `Backend/CoreSRE/Endpoints/ToolEndpoints.cs` — map to `GetAvailableFunctionsQuery`, return `ApiResult<IEnumerable<BindableToolDto>>` per `contracts/available-functions-api.md`

### Frontend — API & Types

- [ ] T031 [P] [US1] Add `BindableTool` type in `Frontend/src/types/tool.ts` with fields: `id: string`, `name: string`, `description?: string`, `toolType: "RestApi" | "McpTool"`, `parentName?: string`, `status: "Active" | "Inactive"`
- [ ] T032 [P] [US1] Add `getAvailableFunctions(params?: { search?: string; status?: string })` API function in `Frontend/src/lib/api/tools.ts` — GET `/api/tools/available-functions`

### Frontend — Tool Picker Component

- [ ] T033 [US1] Create `ToolRefsPicker.tsx` in `Frontend/src/components/agents/ToolRefsPicker.tsx` — searchable multi-select component using shadcn `Popover` + `Command` pattern. Features: (a) trigger button showing count of selected tools, (b) `CommandInput` for search filtering, (c) `CommandGroup` per tool type (REST API / MCP Tool), (d) `CommandItem` with checkbox, tool name, description truncated, type badge, (e) selected items rendered as removable `Badge` tags below the trigger, (f) fetches data from `getAvailableFunctions()` on mount. Props: `value: string[]` (selected IDs), `onChange: (ids: string[]) => void`.
- [ ] T034 [US1] Modify `LlmConfigSection.tsx` in `Frontend/src/components/agents/LlmConfigSection.tsx` — replace the raw GUID text `<Input>` for ToolRefs with `<ToolRefsPicker value={toolRefs} onChange={setToolRefs} />`. In view mode, replace GUID badges with resolved tool name badges (fetch tool names from available-functions API or pass pre-resolved names).
- [ ] T035 [US1] Handle empty state in `ToolRefsPicker.tsx` — when `getAvailableFunctions()` returns empty list, show "暂无可用工具，请先注册工具" message in the Command list.
- [ ] T036 [US1] Handle tool count warning in `ToolRefsPicker.tsx` — when selected count exceeds 20, show amber warning text "绑定超过 20 个工具可能影响 LLM 上下文，建议精简" below the selected badges (non-blocking).

**Checkpoint**: Admins can visually select and manage tool bindings. GUID input replaced with searchable picker.

---

## Phase 6: User Story 4 — Agent 详情页工具绑定概览 (Priority: P2)

**Goal**: Agent detail page shows a read-only list of bound tools with names, types, status badges, and links to tool detail pages.

**Independent Test**: View a ChatClient agent detail page with bound tools → see tool cards with correct info → click card → navigate to tool detail.

- [ ] T037 [P] [US4] Create `BoundToolsSection.tsx` in `Frontend/src/components/agents/BoundToolsSection.tsx` — accepts `toolRefs: string[]` prop, fetches tool details from `getAvailableFunctions()` filtered to matching IDs, renders a read-only card list. Each card shows: tool name, type badge (REST API blue / MCP Tool purple), status badge (Active green / Inactive amber with warning icon), description snippet. Card is clickable → navigates to `/tools/{id}`.
- [ ] T038 [US4] Integrate `BoundToolsSection` into `AgentDetailPage.tsx` in `Frontend/src/pages/AgentDetailPage.tsx` — for ChatClient agents with non-empty `LlmConfig.toolRefs`, render `<BoundToolsSection toolRefs={agent.llmConfig.toolRefs} />` in the agent detail layout.
- [ ] T039 [US4] Handle edge case: deleted tool refs in `BoundToolsSection.tsx` — when a toolRef ID is not found in the available-functions response, show a "工具已删除" greyed-out placeholder card instead of hiding it.

**Checkpoint**: Agent detail page shows bound tools with full context. Admins can verify bindings and navigate to tool details.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Edge cases, validation, documentation, and cleanup.

- [ ] T040 [P] Add edge case handling in `ToolFunctionFactory.cs` — when a matched ToolRegistration has Status=Inactive, still create the AIFunction (per spec: LLM may call, invocation returns error). Log info-level message.
- [ ] T041 [P] Add tool call error resilience in `AgentChatEndpoints.cs` — ensure that if `FunctionCallContent` is detected but has no `CallId`, generate a fallback ID. Ensure no unhandled exceptions from tool call SSE emission break the streaming loop.
- [ ] T042 [P] Update `CoreSRE.http` test file in `Backend/CoreSRE/CoreSRE.http` — add sample requests for `GET /api/tools/available-functions` and `POST /api/chat/stream` with a tool-bound agent for manual integration testing.
- [ ] T043 Run full test suite (`Backend/CoreSRE.Application.Tests/` + `Backend/CoreSRE.Infrastructure.Tests/`) — verify all existing tests still pass (no regressions) and all new tests pass.
- [ ] T044 Run quickstart.md end-to-end validation — follow the 10-step flow in `specs/010-chatclient-tool-binding/quickstart.md` and verify each step works correctly.

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1 (Setup) ─────► Phase 2 (Foundational) ─────┬──► Phase 3 (US2: Backend Pipeline) ──► Phase 4 (US3: Visualization)
                                                     │
                                                     └──► Phase 5 (US1: Tool Picker) ──► Phase 6 (US4: Detail Page)
                                                     
All Phases ──► Phase 7 (Polish)
```

- **Phase 1 (Setup)**: No dependencies
- **Phase 2 (Foundational)**: Depends on Phase 1 — **BLOCKS all user stories**
- **Phase 3 (US2)**: Depends on Phase 2 — needs `IToolFunctionFactory` and `GetByIdsAsync()`
- **Phase 4 (US3)**: Depends on Phase 3 — needs FunctionInvokingChatClient producing `FunctionCallContent`/`FunctionResultContent` in stream
- **Phase 5 (US1)**: Depends on Phase 2 — independent of US2/US3 (frontend picker + new API endpoint)
- **Phase 6 (US4)**: Depends on Phase 5 — reuses `getAvailableFunctions()` API
- **Phase 7 (Polish)**: Depends on all desired story phases

### User Story Dependencies

- **US2 (Backend Pipeline)**: Independent. Can start after Foundational.
- **US3 (Visualization)**: Depends on US2 (needs tool calls in streaming output to visualize)
- **US1 (Tool Picker)**: Independent. Can start after Foundational. Parallel with US2.
- **US4 (Detail Page)**: Depends on US1 (reuses available-functions API + BindableTool type)

### Within Each Phase

- Tests MUST be written and FAIL before implementation (Red-Green-Refactor)
- Interfaces before implementations
- Models/types before services
- Services before endpoints/components
- Core implementation before integration

### Parallel Opportunities

**After Phase 2 completes, two parallel tracks are possible:**

```
Track A: US2 (T015–T019) ──► US3 (T020–T027)    [Backend pipeline → Visualization]
Track B: US1 (T028–T036) ──► US4 (T037–T039)    [Tool picker → Detail page]
```

Within phases:
- T002, T003, T004 can run in parallel (different test files)
- T005, T006, T007 can run in parallel (different interface files)
- T008, T009 can run in parallel (different repository files)
- T024, T025 can start in parallel (types vs hook — different files)
- T031, T032 can run in parallel (types vs API — different files)

---

## Parallel Example: Foundational Phase

```
# All test files can be written simultaneously:
T002: ToolFunctionFactoryTests.cs
T003: ToolRegistrationAIFunctionTests.cs
T004: McpToolAIFunctionTests.cs

# All interface additions can be made simultaneously:
T005: IToolRegistrationRepository.cs (add method)
T006: IMcpToolItemRepository.cs (add method)
T007: IToolFunctionFactory.cs (new file)

# Repository implementations can be done simultaneously:
T008: ToolRegistrationRepository.cs
T009: McpToolItemRepository.cs
```

---

## Implementation Strategy

### MVP First (US2 Only — Backend Tool Invocation)

1. Phase 1: Setup (T001)
2. Phase 2: Foundational (T002–T014)
3. Phase 3: US2 — LLM Tool Auto-Invocation (T015–T019)
4. **STOP**: Agent conversations now silently use tools (no frontend visibility). Verify via API/logs.

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. **Add US2** → Backend pipeline works → Test via API (MVP backend!)
3. **Add US3** → Tool calls visible in chat UI → Full end-to-end demo
4. **Add US1** (parallel with US2/US3) → Admin tool picker UX → Config experience improved
5. **Add US4** → Agent detail page shows tools → Admin overview complete
6. Polish → Edge cases, docs, full test pass

### Parallel Team Strategy

After Foundational phase:
- **Developer A**: US2 (T015–T019) → US3 (T020–T027) [backend + visualization]
- **Developer B**: US1 (T028–T036) → US4 (T037–T039) [picker + detail page]

---

## Summary

| Metric | Count |
|--------|-------|
| Total tasks | 44 |
| Phase 1 (Setup) | 1 |
| Phase 2 (Foundational) | 13 |
| Phase 3 (US2 — Backend Pipeline) | 5 |
| Phase 4 (US3 — Visualization) | 8 |
| Phase 5 (US1 — Tool Picker) | 9 |
| Phase 6 (US4 — Detail Page) | 3 |
| Phase 7 (Polish) | 5 |
| Parallelizable tasks | 18 |
| Test tasks | 5 (T002, T003, T004, T015, T043) |

## Notes

- All task IDs are sequential (T001–T044) in execution order
- [P] marks tasks safe for parallel execution
- [US#] labels appear only in user story phases (Phases 3–6)
- Commit after each task or logical group
- Each checkpoint verifies the story is independently testable
- Foundational phase is the critical path — optimize for speed here
