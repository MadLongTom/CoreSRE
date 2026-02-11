# Available Tool Functions API — Contract

**Version**: 1.0

## New Endpoint

### GET /api/tools/available-functions

Returns a flat list of all bindable tool functions for the tool picker UI. Combines REST API tools and MCP sub-tools into a single response.

**Purpose**: Eliminates N+1 API calls on the frontend (currently would need `getTools()` + `getMcpTools(id)` per MCP server).

#### Response

```json
{
  "data": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "name": "check-service-health",
      "description": "Checks the health status of a microservice",
      "toolType": "RestApi",
      "parentName": null,
      "status": "Active"
    },
    {
      "id": "7b4c3d2e-1a0f-4e8b-9c7d-6e5f4a3b2c1d",
      "name": "query_database",
      "description": "Runs a read-only SQL query against the analytics database",
      "toolType": "McpTool",
      "parentName": "Analytics MCP Server",
      "status": "Active"
    }
  ],
  "success": true,
  "error": null
}
```

#### Response Schema

```typescript
interface AvailableFunctionsResponse {
  data: BindableTool[];
  success: boolean;
  error: string | null;
}

interface BindableTool {
  id: string;           // ToolRegistration.Id (RestApi) or McpToolItem.Id (McpTool)
  name: string;         // ToolRegistration.Name or McpToolItem.ToolName
  description?: string; // Tool description
  toolType: "RestApi" | "McpTool";  // Note: "McpTool" not "McpServer" — this is a single tool
  parentName?: string;  // null for RestApi; MCP server name for McpTool
  status: "Active" | "Inactive";
}
```

#### Query Parameters

| Param | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `status` | `string` | No | `Active` | Filter by status. Use `all` for both. |
| `search` | `string` | No | — | Case-insensitive name/description search |

#### Notes

- **RestApi** tools: each `ToolRegistration` with `ToolType=RestApi` becomes one entry. The `id` is `ToolRegistration.Id`.
- **McpTool** entries: each `McpToolItem` belonging to an active `McpServer` becomes one entry. The `id` is `McpToolItem.Id`. `parentName` is the parent `ToolRegistration.Name`.
- Only tools with `Status=Active` are returned by default (Inactive tools cannot be reliably called).
- Sorting: alphabetical by `name` within each `toolType` group.

---

## Existing Endpoint Unchanged

### POST /api/chat/stream

No input schema changes. The `AgentChatInput` DTO remains the same.

The only change is in **output**: the SSE `text/event-stream` response will now include `TOOL_CALL_START`, `TOOL_CALL_ARGS`, and `TOOL_CALL_END` events (see [agui-tool-events.md](agui-tool-events.md)).
