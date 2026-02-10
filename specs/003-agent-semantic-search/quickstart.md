# Quick Start: Agent 能力语义搜索

**Feature**: 003-agent-semantic-search | **Priority**: P1 (keyword search)

## Prerequisites

- SPEC-001 Agent Registry CRUD 已实现并可运行
- Aspire AppHost 启动（PostgreSQL 容器自动创建）
- 系统中已注册至少 1 个 A2A Agent（含 skills）

## Step 1: 注册测试 Agent

注册 3 个 A2A Agent，各含不同 skills，用于验证搜索功能。

```bash
# Agent A — customer-related skills
curl -X POST https://localhost:14367/api/agents \
  -H "Content-Type: application/json" \
  -d '{
    "name": "CustomerSupportAgent",
    "agentType": "A2A",
    "agentCard": {
      "skills": [
        { "name": "answer-customer-questions", "description": "Answer questions about products and orders" },
        { "name": "customer-onboarding", "description": "Guide new customers through setup" }
      ],
      "interfaces": [{ "protocol": "HTTPS" }],
      "securitySchemes": []
    }
  }'

# Agent B — code-related skills (no "customer" keyword)
curl -X POST https://localhost:14367/api/agents \
  -H "Content-Type: application/json" \
  -d '{
    "name": "CodeReviewAgent",
    "agentType": "A2A",
    "agentCard": {
      "skills": [
        { "name": "code-review", "description": "Review code for bugs and style issues" },
        { "name": "refactoring-suggestions", "description": "Suggest code improvements and refactoring" }
      ],
      "interfaces": [{ "protocol": "HTTPS" }],
      "securitySchemes": []
    }
  }'

# Agent C — mixed skills (one matches "customer")
curl -X POST https://localhost:14367/api/agents \
  -H "Content-Type: application/json" \
  -d '{
    "name": "OnboardingAgent",
    "agentType": "A2A",
    "agentCard": {
      "skills": [
        { "name": "new-employee-onboarding", "description": "Help new employees get started" },
        { "name": "customer-feedback", "description": "Collect and analyze customer feedback" }
      ],
      "interfaces": [{ "protocol": "HTTPS" }],
      "securitySchemes": []
    }
  }'
```

## Step 2: 关键词搜索

```bash
# 搜索 "customer" — 应返回 Agent A (2 matched skills) 和 Agent C (1 matched skill)
curl -s https://localhost:14367/api/agents/search?q=customer | jq .
```

**预期响应**:
```json
{
  "results": [
    {
      "id": "<agent-a-id>",
      "name": "CustomerSupportAgent",
      "agentType": "A2A",
      "status": "Active",
      "createdAt": "...",
      "matchedSkills": [
        { "name": "answer-customer-questions", "description": "Answer questions about products and orders" },
        { "name": "customer-onboarding", "description": "Guide new customers through setup" }
      ],
      "similarityScore": null
    },
    {
      "id": "<agent-c-id>",
      "name": "OnboardingAgent",
      "agentType": "A2A",
      "status": "Active",
      "createdAt": "...",
      "matchedSkills": [
        { "name": "customer-feedback", "description": "Collect and analyze customer feedback" }
      ],
      "similarityScore": null
    }
  ],
  "searchMode": "keyword",
  "query": "customer",
  "totalCount": 2
}
```

**验证点**:
- ✅ Agent A 排在 Agent C 前面（2 matched skills > 1 matched skill）
- ✅ Agent B 不在结果中（无 "customer" 匹配）
- ✅ `searchMode` 为 `"keyword"`
- ✅ 每个 result 包含 `matchedSkills` 列表

## Step 3: 验证边界条件

```bash
# 大小写不敏感 — 应返回与 "customer" 相同的结果
curl -s "https://localhost:14367/api/agents/search?q=CUSTOMER" | jq .totalCount
# 预期: 2

# 无匹配 — 应返回空列表
curl -s "https://localhost:14367/api/agents/search?q=quantum" | jq .
# 预期: { "results": [], "searchMode": "keyword", "query": "quantum", "totalCount": 0 }

# 缺少 q 参数 — 应返回 400
curl -s -o /dev/null -w "%{http_code}" "https://localhost:14367/api/agents/search"
# 预期: 400

# 空字符串 — 应返回 400
curl -s -o /dev/null -w "%{http_code}" "https://localhost:14367/api/agents/search?q="
# 预期: 400

# 搜索 "code" — 应只返回 Agent B
curl -s "https://localhost:14367/api/agents/search?q=code" | jq '.results[].name'
# 预期: "CodeReviewAgent"
```

## Step 4: 验证非 A2A Agent 不出现在结果中

```bash
# 注册一个 ChatClient Agent（无 AgentCard/skills）
curl -X POST https://localhost:14367/api/agents \
  -H "Content-Type: application/json" \
  -d '{ "name": "ChatBot", "agentType": "ChatClient" }'

# 搜索应不包含 ChatClient Agent
curl -s "https://localhost:14367/api/agents/search?q=chat" | jq '.results[].name'
# 预期: 空（ChatClient 无 skills，不参与搜索）
```
