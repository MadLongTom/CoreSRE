# AG-UI Tool Call Events — SSE Contract

**Version**: 1.0 | **Protocol**: AG-UI (SSE)

## Event Types

All events are sent as SSE `data:` lines in `text/event-stream` response from `POST /api/chat/stream`.

### TOOL_CALL_START

Emitted when the LLM decides to call a tool (detected via `FunctionCallContent` in streaming response).

```json
{
  "type": "TOOL_CALL_START",
  "toolCallId": "string",
  "toolCallName": "string",
  "parentMessageId": "string"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | `string` | Yes | Always `"TOOL_CALL_START"` |
| `toolCallId` | `string` | Yes | Unique ID for this tool call within the run. Sourced from `FunctionCallContent.CallId`. |
| `toolCallName` | `string` | Yes | Name of the tool being called. Sourced from `FunctionCallContent.Name`. |
| `parentMessageId` | `string` | Yes | ID of the assistant message that initiated the tool call. |

### TOOL_CALL_ARGS

Emitted immediately after `TOOL_CALL_START` with the tool call arguments.

```json
{
  "type": "TOOL_CALL_ARGS",
  "toolCallId": "string",
  "delta": "string"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | `string` | Yes | Always `"TOOL_CALL_ARGS"` |
| `toolCallId` | `string` | Yes | Must match a preceding `TOOL_CALL_START.toolCallId`. |
| `delta` | `string` | Yes | JSON-serialized arguments string. Sent as a single chunk (not incremental). |

### TOOL_CALL_END

Emitted when the tool call completes (detected via `FunctionResultContent` in streaming response).

```json
{
  "type": "TOOL_CALL_END",
  "toolCallId": "string"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | `string` | Yes | Always `"TOOL_CALL_END"` |
| `toolCallId` | `string` | Yes | Must match a preceding `TOOL_CALL_START.toolCallId`. |

## Event Ordering

### Single Tool Call Sequence

```
RUN_STARTED
TEXT_MESSAGE_START (messageId: "msg_1", role: "assistant")
TOOL_CALL_START   (toolCallId: "call_1", toolCallName: "get_weather", parentMessageId: "msg_1")
TOOL_CALL_ARGS    (toolCallId: "call_1", delta: '{"location":"Seattle"}')
TOOL_CALL_END     (toolCallId: "call_1")
TEXT_MESSAGE_CONTENT (messageId: "msg_1", delta: "The weather in Seattle is...")
TEXT_MESSAGE_END  (messageId: "msg_1")
RUN_FINISHED
```

### Multiple Tool Calls (Sequential)

```
RUN_STARTED
TEXT_MESSAGE_START
TOOL_CALL_START   (call_1, "get_weather")
TOOL_CALL_ARGS    (call_1)
TOOL_CALL_END     (call_1)
TOOL_CALL_START   (call_2, "get_time")
TOOL_CALL_ARGS    (call_2)
TOOL_CALL_END     (call_2)
TEXT_MESSAGE_CONTENT (final response using both results)
TEXT_MESSAGE_END
RUN_FINISHED
```

### Constraints

1. `TOOL_CALL_START` MUST precede `TOOL_CALL_ARGS` and `TOOL_CALL_END` for the same `toolCallId`
2. `TOOL_CALL_ARGS` MUST appear exactly once between START and END for each `toolCallId`
3. `TOOL_CALL_END` MUST appear exactly once per `TOOL_CALL_START`
4. Text message events (`TEXT_MESSAGE_CONTENT`) appear AFTER all tool calls complete (in the same or subsequent iteration)
5. All events MUST be within a single `RUN_STARTED`/`RUN_FINISHED` pair
6. On tool execution error, `TOOL_CALL_END` is still emitted (the error feeds back to LLM via the function-calling loop)
