# Quickstart: 工作流引擎基础修复

**Feature**: 015-workflow-engine-fix | **Branch**: `015-workflow-engine-fix`

## Prerequisites

- .NET 10 SDK installed
- PostgreSQL running (for integration; unit tests use mocks)
- Repository cloned and on branch `015-workflow-engine-fix`

## What Changed

### 1. Node Input Recording (D5 Fix)

**Problem**: `NodeExecutionVO.Input` was never written — every node execution had `null` input.

**Fix**: `WorkflowExecution.StartNode(nodeId, input)` now accepts an `input` parameter and writes it to the `NodeExecutionVO` record. All 5 call sites in `WorkflowEngine` pass the current input data.

**Verify**:
```bash
cd Backend
dotnet test CoreSRE.Infrastructure.Tests --filter "FullyQualifiedName~NodeInputRecording"
```

### 2. GraphSnapshot in API Response

**Problem**: `WorkflowExecutionDto` was missing the `GraphSnapshot` property — the execution detail API returned no graph structure.

**Fix**: Added `WorkflowGraphDto? GraphSnapshot` to `WorkflowExecutionDto`. AutoMapper picks up the existing `WorkflowGraphVO → WorkflowGraphDto` mapping automatically.

**Verify**:
```bash
cd Backend
dotnet test CoreSRE.Application.Tests --filter "FullyQualifiedName~GraphSnapshot"
```

### 3. Mock Agent Mode

**Problem**: No way to execute workflows without a real LLM provider configured.

**Fix**: New `MockChatClient` implements `IChatClient`. `AgentResolverService` falls back to `MockChatClient` when:
- The `Workflow:MockAgentMode` config is set to `true`, OR
- No `LlmProvider` is found for the agent's configured provider

Mock responses are JSON containing `agentName`, `inputSummary`, and `timestamp`.

**Verify**:
```bash
cd Backend
dotnet test CoreSRE.Infrastructure.Tests --filter "FullyQualifiedName~MockAgent"
```

### 4. End-to-End Smoke Test

```bash
cd Backend
dotnet test CoreSRE.Infrastructure.Tests --filter "FullyQualifiedName~EndToEndSmoke"
```

## Run All Tests

```bash
cd Backend
dotnet test CoreSRE.Infrastructure.Tests
dotnet test CoreSRE.Application.Tests
```

## Configuration

### Enable Mock Agent Mode (optional)

In `appsettings.Development.json`:
```json
{
  "Workflow": {
    "MockAgentMode": true
  }
}
```

Or via environment variable:
```bash
export Workflow__MockAgentMode=true
```

When enabled, ALL agent nodes in workflows use mock responses. For production, leave this setting absent or set to `false`.

## API Response Change

`GET /api/workflows/{id}/executions/{execId}` now includes:
- `graphSnapshot` — full DAG structure (nodes + edges) at execution time
- `nodeExecutions[].input` — populated with the actual input data each node received

See [contracts/execution-detail-response.json](contracts/execution-detail-response.json) for the complete response schema.

## Files Modified

| File | Change |
|------|--------|
| `Domain/Entities/WorkflowExecution.cs` | `StartNode(nodeId)` → `StartNode(nodeId, input)` |
| `Application/Workflows/DTOs/WorkflowExecutionDto.cs` | Added `GraphSnapshot` property |
| `Infrastructure/Services/WorkflowEngine.cs` | Pass input to `StartNode` at all 5 call sites |
| `Infrastructure/Services/AgentResolverService.cs` | Mock mode fallback |
| `Infrastructure/Services/MockChatClient.cs` | NEW — `IChatClient` mock implementation |
