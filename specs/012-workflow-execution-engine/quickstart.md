# Quickstart: 工作流执行引擎

**Feature**: 012-workflow-execution-engine  
**Date**: 2026-02-11

---

## Prerequisites

1. SPEC-020（工作流定义 CRUD）已实现 — WorkflowDefinition、WorkflowGraphVO 等已存在
2. PostgreSQL 数据库运行中（通过 Aspire AppHost）
3. 至少一个已注册的 AgentRegistration（用于 Agent 节点执行）

---

## Step 1: 创建并发布一个顺序工作流

```http
### 创建工作流（3 个 Agent 顺序串联）
POST /api/workflows
Content-Type: application/json

{
  "name": "Sequential Health Check",
  "description": "顺序检查各服务健康状态",
  "graph": {
    "nodes": [
      { "nodeId": "agent-a", "nodeType": "Agent", "referenceId": "<agent-registration-id-1>", "displayName": "收集信息" },
      { "nodeId": "agent-b", "nodeType": "Agent", "referenceId": "<agent-registration-id-2>", "displayName": "分析状态" },
      { "nodeId": "agent-c", "nodeType": "Agent", "referenceId": "<agent-registration-id-3>", "displayName": "生成报告" }
    ],
    "edges": [
      { "edgeId": "e1", "sourceNodeId": "agent-a", "targetNodeId": "agent-b", "edgeType": "Normal" },
      { "edgeId": "e2", "sourceNodeId": "agent-b", "targetNodeId": "agent-c", "edgeType": "Normal" }
    ]
  }
}
```

发布工作流（需先通过 SPEC-026 或手动更新状态为 Published）。

---

## Step 2: 执行工作流

```http
### 启动执行
POST /api/workflows/{workflowId}/execute
Content-Type: application/json

{
  "input": {
    "query": "检查所有服务的健康状态"
  }
}
```

**Expected Response** (202 Accepted):
```json
{
  "success": true,
  "data": {
    "id": "<execution-id>",
    "status": "Pending",
    "nodeExecutions": [
      { "nodeId": "agent-a", "status": "Pending" },
      { "nodeId": "agent-b", "status": "Pending" },
      { "nodeId": "agent-c", "status": "Pending" }
    ]
  }
}
```

---

## Step 3: 查询执行状态

```http
### 查询执行详情（轮询直到 Completed/Failed）
GET /api/workflows/{workflowId}/executions/{executionId}
```

**执行中 Response** (200 OK):
```json
{
  "success": true,
  "data": {
    "status": "Running",
    "nodeExecutions": [
      { "nodeId": "agent-a", "status": "Completed", "output": "{\"info\": \"...\"}" },
      { "nodeId": "agent-b", "status": "Running", "input": "{\"info\": \"...\"}" },
      { "nodeId": "agent-c", "status": "Pending" }
    ]
  }
}
```

**完成 Response**:
```json
{
  "success": true,
  "data": {
    "status": "Completed",
    "output": { "report": "所有服务健康" },
    "nodeExecutions": [
      { "nodeId": "agent-a", "status": "Completed" },
      { "nodeId": "agent-b", "status": "Completed" },
      { "nodeId": "agent-c", "status": "Completed" }
    ]
  }
}
```

---

## Step 4: 查询执行历史列表

```http
### 列出该工作流的所有执行记录
GET /api/workflows/{workflowId}/executions

### 按状态过滤
GET /api/workflows/{workflowId}/executions?status=Failed
```

---

## Scenario: 并行编排（FanOut/FanIn）

```http
POST /api/workflows
Content-Type: application/json

{
  "name": "Parallel Data Collection",
  "graph": {
    "nodes": [
      { "nodeId": "start", "nodeType": "FanOut", "displayName": "并行分发" },
      { "nodeId": "log-agent", "nodeType": "Agent", "referenceId": "<id>", "displayName": "查询日志" },
      { "nodeId": "metric-agent", "nodeType": "Agent", "referenceId": "<id>", "displayName": "查询指标" },
      { "nodeId": "config-agent", "nodeType": "Agent", "referenceId": "<id>", "displayName": "查询配置" },
      { "nodeId": "aggregate", "nodeType": "FanIn", "displayName": "结果聚合" }
    ],
    "edges": [
      { "edgeId": "e1", "sourceNodeId": "start", "targetNodeId": "log-agent", "edgeType": "Normal" },
      { "edgeId": "e2", "sourceNodeId": "start", "targetNodeId": "metric-agent", "edgeType": "Normal" },
      { "edgeId": "e3", "sourceNodeId": "start", "targetNodeId": "config-agent", "edgeType": "Normal" },
      { "edgeId": "e4", "sourceNodeId": "log-agent", "targetNodeId": "aggregate", "edgeType": "Normal" },
      { "edgeId": "e5", "sourceNodeId": "metric-agent", "targetNodeId": "aggregate", "edgeType": "Normal" },
      { "edgeId": "e6", "sourceNodeId": "config-agent", "targetNodeId": "aggregate", "edgeType": "Normal" }
    ]
  }
}
```

执行后 FanIn 节点接收三个 Agent 输出的聚合数组。

---

## Scenario: 条件分支

```http
POST /api/workflows
Content-Type: application/json

{
  "name": "Conditional Alert Routing",
  "graph": {
    "nodes": [
      { "nodeId": "classify", "nodeType": "Agent", "referenceId": "<id>", "displayName": "告警分类" },
      { "nodeId": "router", "nodeType": "Condition", "displayName": "严重度路由" },
      { "nodeId": "urgent", "nodeType": "Agent", "referenceId": "<id>", "displayName": "紧急处理" },
      { "nodeId": "routine", "nodeType": "Agent", "referenceId": "<id>", "displayName": "常规处理" }
    ],
    "edges": [
      { "edgeId": "e1", "sourceNodeId": "classify", "targetNodeId": "router", "edgeType": "Normal" },
      { "edgeId": "e2", "sourceNodeId": "router", "targetNodeId": "urgent", "edgeType": "Conditional", "condition": "$.severity == \"high\"" },
      { "edgeId": "e3", "sourceNodeId": "router", "targetNodeId": "routine", "edgeType": "Conditional", "condition": "$.severity == \"low\"" }
    ]
  }
}
```

当 classify Agent 输出 `{"severity": "high"}` 时，路由到 urgent Agent；输出 `{"severity": "low"}` 时路由到 routine Agent。未命中的分支节点标记为 Skipped。

---

## Error Scenarios

### Draft 工作流执行
```http
POST /api/workflows/{draft-workflow-id}/execute
→ 400 Bad Request: "仅已发布的工作流可执行。当前状态: Draft"
```

### 不存在的工作流
```http
POST /api/workflows/{nonexistent-id}/execute
→ 404 Not Found
```

### Agent 引用无效
```http
POST /api/workflows/{id-with-deleted-agent}/execute
→ 400 Bad Request: "工作流引用的外部资源不存在"
```
