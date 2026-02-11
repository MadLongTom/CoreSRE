# Quickstart: ChatClient 工具绑定与对话调用

**Branch**: `010-chatclient-tool-binding`

## What This Feature Does

Enables ChatClient agents to bind registered tools (REST API or MCP sub-tools) and automatically invoke them during LLM conversations via Function Calling. Tool calls are visualized in real-time in the chat UI.

## End-to-End Flow

```
1. Admin: Open Agent edit page
2. Admin: Click "Add Tools" → tool picker opens
3. Admin: Search & select tools → save agent
4. User: Chat with agent, ask question requiring tool
5. LLM: Decides to call tool (Function Calling)
6. Backend: Executes tool via IToolInvoker
7. Backend: Streams TOOL_CALL_START/ARGS/END via SSE
8. Frontend: Renders tool call card (calling → completed)
9. LLM: Receives tool result, generates final answer
10. Frontend: Shows final text response after tool cards
```

## Key Architecture Decisions

| Decision | Choice | See Research |
|----------|--------|-------------|
| AIFunction creation | Subclass `AIFunction` (no factory for dynamic schema) | R1 |
| IChatClient pipeline | `FunctionInvokingChatClient` via `.UseFunctionInvocation()` | R2 |
| Streaming detection | Check `FunctionCallContent` / `FunctionResultContent` in stream | R3 |
| AG-UI events | `TOOL_CALL_START` / `TOOL_CALL_ARGS` / `TOOL_CALL_END` | R4 |
| Batch fetch | New `GetByIdsAsync()` on both repositories | R5 |
| Conversion service | `IToolFunctionFactory` (Application) → `ToolFunctionFactory` (Infrastructure) | R6 |
| Schema handling | Normalize both `string?` and `JsonElement?` to `JsonElement` | R7 |
| Tool picker UX | Combobox multi-select with shadcn `Command` | R8 |
| Tool call card | Collapsible card with status transitions | R9 |

## Implementation Layers

### Backend (bottom-up)

1. **Repository layer**: Add `GetByIdsAsync()` to `IToolRegistrationRepository` + `IMcpToolItemRepository`
2. **Application interfaces**: Add `IToolFunctionFactory`
3. **Infrastructure services**: 
   - `ToolRegistrationAIFunction` / `McpToolAIFunction` — custom AIFunction subclasses
   - `ToolFunctionFactory` — resolves tool refs to AIFunction list
4. **Agent resolver**: Modify `AgentResolverService.ResolveChatClientAgent()` to call `IToolFunctionFactory`, wrap `IChatClient` with `FunctionInvokingChatClient`, pass tools via `ChatOptions`
5. **Chat endpoint**: Modify `AgentChatEndpoints.HandleChatClientStreamAsync()` to detect `FunctionCallContent`/`FunctionResultContent` and emit `TOOL_CALL_*` SSE events
6. **Available functions endpoint**: New `GET /api/tools/available-functions` for frontend tool picker

### Frontend (bottom-up)

1. **Types**: Extend `ChatMessage` with `ToolCall[]`, add `BindableTool` type
2. **API**: Add `getAvailableFunctions()` API call
3. **Hook**: Extend `use-agent-chat.ts` to parse `TOOL_CALL_*` events and update message state
4. **Components**:
   - `ToolCallCard.tsx` — collapsible card for tool call visualization
   - `ToolRefsPicker.tsx` — searchable multi-select combo for agent config
   - Update `MessageBubble.tsx` to render tool call cards
   - Update `LlmConfigSection.tsx` to use `ToolRefsPicker`

## Testing Strategy

| Layer | Focus | Framework |
|-------|-------|-----------|
| Application | `IToolFunctionFactory` contract, edge cases (deleted refs, empty refs) | xUnit + Moq |
| Infrastructure | `ToolFunctionFactory` integration, `ToolRegistrationAIFunction` invocation | xUnit + Moq |
| Infrastructure | `AgentResolverService` tool pipeline wiring | xUnit + Moq |
| API (manual) | AG-UI SSE event ordering and content | Manual + HTTP file |
| Frontend (manual) | Tool picker UX, tool call card rendering | Manual browser testing |

## Dependencies

- **No new NuGet packages** — `Microsoft.Extensions.AI` is already transitively available
- **No database migrations** — `LlmConfig.ToolRefs` already exists as JSONB
- **No new npm packages** — shadcn `Command`/`Popover` already available
