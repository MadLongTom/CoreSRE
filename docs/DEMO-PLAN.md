# CoreSRE 稳定演示计划

> **目标**: 端到端展示 CoreSRE 作为 AIOps 智能体编排平台的核心能力，从基础设施搭建到告警闭环全流程可复现演示。  
> **预计时长**: 40–50 分钟（含交互）  
> **前置条件**: Docker Desktop + Kubernetes、.NET 10 SDK、Node.js 20+

---

## 一、演示环境准备（演示前完成）

### 1.1 启动后端 + 前端开发环境

```powershell
.\dev.ps1
# → Aspire Dashboard: https://localhost:17178
# → Frontend:         http://localhost:5173
# → CoreSRE API:      http://localhost:5156
```

### 1.2 部署 K8s 可观测性栈 + 模拟业务系统

```powershell
.\deploy-demo.ps1
# 自动部署:
#   observability namespace: Prometheus / Loki+Promtail / Jaeger / Alertmanager
#   demo-app namespace:      order-service / payment-service / inventory-service / traffic-generator
# 自动注册 5 个数据源 + 3 条告警规则到 CoreSRE
```

### 1.3 环境检查清单

| 项目 | 验证方式 | 预期 |
|------|---------|------|
| CoreSRE API | `curl http://localhost:5156/api/agents` | 200 OK |
| Frontend | 浏览器打开 `http://localhost:5173` | Agent 列表页 |
| Aspire Dashboard | `https://localhost:17178` | 显示 PostgreSQL + API |
| Prometheus | `http://localhost:30090/targets` | demo-app targets UP |
| Jaeger | `http://localhost:30686` | 有 order/payment/inventory service |
| Alertmanager | `http://localhost:30093/#/alerts` | 告警规则就绪 |
| Demo 流量 | `kubectl logs -n demo-app -l app=traffic-generator -f` | 持续产生请求 |

---

## 二、演示脚本（6 个场景）

---

### 场景 1: 平台概览 — "一个统一的 AI 运维平台"（5 min）

**演示目标**: 展示全局视图，建立产品认知

**步骤**:

1. **打开前端首页** (`/agents`)
   - 展示 Agent 注册列表页面，说明三种 Agent 类型（ChatClient / A2A / Team）
   - 强调这是所有 AI 能力的统一注册中心

2. **浏览导航栏**，依次点击每个模块简要说明:
   - 💡 **Agents** — 智能体注册与管理
   - 🔧 **Tools** — 工具网关（REST API / MCP 协议统一接入）
   - 📋 **Workflows** — 可视化 DAG 工作流编排
   - 📊 **Data Sources** — 可观测性数据源（Prometheus / Loki / Jaeger）
   - 🛡️ **Skills (SOPs)** — 标准操作流程生命周期管理
   - 🚨 **Alert Rules** — 告警规则路由配置
   - 📈 **Evaluation** — SOP 效果评估看板

3. **切换到 Aspire Dashboard** (`https://localhost:17178`)
   - 展示服务拓扑：PostgreSQL + CoreSRE API
   - 展示 OpenTelemetry Traces / Logs 集成

**演示话术**: "CoreSRE 是一个分布式 AI 智能体编排平台，将 Agent 注册、工具管理、工作流编排、告警响应、SOP 执行整合在一个统一平面上。"

---

### 场景 2: LLM Provider + Agent 注册（8 min）

**演示目标**: 展示智能体全生命周期管理

#### 2.1 注册 LLM Provider

1. 进入 **Providers** 页面，点击 "Create Provider"
2. 填写表单：
   - Name: `azure-openai-gpt4o`
   - Provider Type: `AzureOpenAI`（或 `OpenAI`）
   - Endpoint / API Key / Model 等配置
3. 保存 → 显示已注册的 Provider 和可用模型列表

#### 2.2 创建 ChatClient Agent（SRE 运维专家）

1. 进入 **Agents > Create**
2. 选择 Agent Type = `ChatClient`
3. 填写：
   - Name: `ops-agent`
   - Description: "SRE 运维专家，擅长故障诊断、指标分析、日志查询"
   - Model: 选择刚注册的模型
   - System Prompt:
     ```
     你是一个 SRE 运维专家。你可以查询 Prometheus 指标、Loki 日志、Jaeger 链路追踪。
     当收到告警时，先查询相关指标确认问题范围，再查询日志定位根因，最后给出修复建议。
     ```
4. 保存 → 查看 Agent 详情页

#### 2.3 创建 Team Agent（事件响应团队）

1. 再次 **Create Agent**，选择 Type = `Team`
2. 填写：
   - Name: `incident-response-team`
   - Team Mode: **Selector**（LLM 动态选择最佳 Agent）
   - 添加 Participants: `ops-agent` + 其他可用 Agent
3. 保存 → 展示 Team Agent 的成员配置界面

**演示话术**: "我们刚配置了一个 SRE 专家 Agent 并将它编入一个 Team。Team 支持 6 种编排模式，Selector 模式下 LLM 会根据对话上下文自动选择最合适的 Agent 应答。"

---

### 场景 3: 工具网关 + Agent 工具绑定（6 min）

**演示目标**: 展示统一工具管理和 Agent 工具绑定

#### 3.1 注册 REST API 工具

1. 进入 **Tools** 页面
2. 展示已有工具（如 datasource-query 等精确工具）
3. **创建新工具**（或通过 OpenAPI Import 演示）：
   - Name: `prometheus-instant-query`
   - Type: `SDK`
   - Description: "查询 Prometheus 即时指标"
   - 配置参数 schema

#### 3.2 绑定工具到 Agent

1. 回到 **Agents > ops-agent > Detail**
2. 在工具绑定区域添加：
   - `prometheus-instant-query`
   - `loki-log-query`
   - `jaeger-trace-query`
3. 保存，展示 Agent 现在拥有的工具列表

**演示话术**: "工具网关统一管理所有外部 API 和 MCP 协议工具，Agent 通过 binding 声明式地获得工具调用能力。一个工具可被多个 Agent 复用。"

---

### 场景 4: 实时对话 — Agent Chat 能力展示（8 min）

**演示目标**: 展示 AG-UI 协议实时 chat 和工具调用

#### 4.1 单 Agent 对话

1. 进入 **Chat** 页面
2. 选择 `ops-agent`
3. 发送消息："查询一下 order-service 过去 5 分钟的 HTTP 请求错误率"
4. **观察**:
   - SSE 实时流式输出
   - Agent 调用 Prometheus 查询工具
   - 返回带格式的指标分析结果
5. 追问："帮我看看 order-service 最近的日志有没有异常"
   - Agent 调用 Loki 日志查询
   - 返回结构化日志分析

#### 4.2 Team Agent 对话

1. 切换到 `incident-response-team`
2. 发送事件描述："payment-service 延迟激增，部分订单支付超时"
3. **观察**:
   - 显示当前由哪个 participant Agent 在应答
   - 各 Agent 按 Selector 模式交替协作
   - 实时显示 participant 归属标签

**演示话术**: "AG-UI 协议支持 SSE 实时流式传输，每条消息都有 Agent 归属标签。Team 模式下多个 Agent 协同分析问题，用户可以清楚看到每个 Agent 的思考过程。"

---

### 场景 5: 可视化工作流编排与执行（8 min）

**演示目标**: 展示 DAG 工作流设计和实时执行追踪

#### 5.1 创建工作流

1. 进入 **Workflows > Create**
2. 设计一个 3 节点 DAG:

```
[查询指标] ──→ [分析异常] ──→ [生成报告]
  (Agent)       (Agent)        (Agent)
```

3. 配置节点:
   - Node 1: `query-metrics` — 类型 Agent，绑定 ops-agent
   - Node 2: `analyze-anomaly` — 类型 Agent，绑定 ops-agent
   - Node 3: `generate-report` — 类型 Agent，绑定 ops-agent
4. 连线（边），设置数据流传递
5. **验证** DAG（无环检测通过）→ 保存

#### 5.2 执行工作流

1. 点击 "Execute"，输入触发参数
2. **实时观察**:
   - SignalR 推送节点执行状态变化
   - 每个节点从 Pending → Running → Completed
   - 展示节点间数据流传递
3. 进入 Execution Detail 查看完整执行历史和每个节点的输出

**演示话术**: "工作流引擎支持 Agent、Tool、条件分支、扇出/扇入等节点类型，工作流也可以被发布为 Workflow Agent，在其他工作流或对话中复用。"

---

### 场景 6: 告警闭环 — 端到端 AIOps 自动化（10 min）⭐ 核心场景

**演示目标**: 展示从告警触发到自动响应的完整闭环

#### 6.1 数据源 & 告警规则预览

1. 打开 **Data Sources** 页面
   - 展示 5 个已注册的数据源（Prometheus / Loki / Jaeger / Alertmanager / K8s）
   - 点击任一数据源查看连接配置和元数据

2. 打开 **Alert Rules** 页面
   - 展示 3 条预配置规则：
     - `HighErrorRate` — P2 → 自动执行 SOP
     - `HighLatency` — P3 → 自动执行 SOP  
     - `ServiceDown` — P1 → 触发 Team Agent 根因分析

#### 6.2 SOP 生命周期

1. 进入 **Skills** 页面
2. 创建一个 SOP:
   - Name: `incident-response`
   - Content: 编写告警响应 SOP 脚本（查询指标 → 判断根因 → 执行修复）
3. 执行生命周期流转: **Draft → Validate → Approve → Publish**
4. 展示 Canary Dry-Run 能力（只读模拟执行，不产生副作用）

#### 6.3 模拟告警触发（核心演示）

1. 手动发送 Alertmanager Webhook 触发告警:

```powershell
$alert = @{
    alerts = @(@{
        status = "firing"
        labels = @{
            alertname = "HighErrorRate"
            severity  = "critical"
            namespace = "demo-app"
            service   = "order-service"
        }
        annotations = @{
            summary     = "order-service HTTP 5xx 错误率超过 10%"
            description = "order-service 在过去 5 分钟内 5xx 错误率达 12.5%"
        }
        startsAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    })
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Uri "http://localhost:5156/api/datasources/webhook/{alertmanager-datasource-id}" `
    -Method Post -ContentType "application/json" -Body $alert
```

2. **实时观察 Incident 创建**:
   - 切换到 **Incidents** 页面
   - 新 Incident 自动创建，状态为 `Triggered`
   - 展示告警详情、匹配的规则、关联的 SOP

3. **SOP 自动执行**:
   - Incident 状态变为 `Executing`
   - Agent 按 SOP 步骤执行:
     1. 查询 Prometheus 确认错误率
     2. 查询 Loki 相关日志
     3. 查询 Jaeger 追踪链路
     4. 生成根因分析结论
   - SignalR 实时推送每个步骤的进度

4. **人工介入（可选演示）**:
   - 如果 SOP 执行中需要审批（如重启服务），展示 Human Intervention 弹窗
   - 人工批准 → 继续执行

5. **完成 & Post-Mortem**:
   - Incident 状态变为 `Resolved`
   - 添加 Post-Mortem 注释
   - 展示 Evaluation Dashboard 上的 SOP 效果数据

**演示话术**: "这就是 CoreSRE 的核心闭环——告警自动触发 SOP，Agent 利用多维数据源进行根因分析并执行修复，全程有 SignalR 实时推送、人工审批门控、事后复盘记录。如果 SOP 失效，系统会自动回退到 Team Agent 进行深度分析。"

---

## 三、应急预案（Demo 稳定性保障）

### 3.1 常见问题 & 解决方案

| 问题 | 解决方案 |
|------|---------|
| K8s Pod 未就绪 | `kubectl get pods -n demo-app -w` 等待，或 `kubectl rollout restart` |
| CoreSRE API 不可达 | 检查 Aspire Dashboard，重启 `.\dev.ps1` |
| Prometheus 无数据 | 确认 traffic-generator 运行中，手动 `curl http://localhost:30090/api/v1/query?query=up` |
| LLM 调用失败 | 确认 Provider API Key 有效，检查网络代理配置 |
| 数据源未注册 | `.\deploy-demo.ps1 -SkipDeploy` 仅重新注册 |
| 告警规则未生效 | 确认 ops-agent 已注册 + SOP 已 Publish，然后重新运行 deploy-demo.ps1 告警规则注册部分 |
| 演示中实时流卡顿 | 准备好预录视频作为 Plan B |

### 3.2 预热脚本（演示前 5 分钟执行）

```powershell
# 确认所有服务健康
curl http://localhost:5156/api/agents          # Agent 列表
curl http://localhost:5156/api/datasources     # 数据源列表
curl http://localhost:5156/api/alert-rules     # 告警规则
curl http://localhost:5156/api/skills          # SOP 列表
curl http://localhost:30090/api/v1/query?query=up  # Prometheus 活跃
kubectl get pods -n demo-app                   # 业务 Pod 状态
kubectl get pods -n observability              # 可观测 Pod 状态
```

### 3.3 预注册演示数据

为保证演示稳定，建议准备一个 `seed-demo-data.ps1` 脚本预先注册:
- 1 个 LLM Provider（带有效 API Key）
- 2 个 ChatClient Agent（ops-agent, log-analyst）
- 1 个 Team Agent（incident-response-team）
- 关键 Tools（prometheus-query, loki-query, jaeger-query）
- 1 个已发布的 SOP（incident-response）
- Agent-Tool 绑定关系

---

## 四、演示流程总览

```
┌──────────────────────────────────────────────────────────────┐
│ 场景 1: 平台概览 (5min)                                       │
│   前端导航 → Aspire Dashboard → 建立产品认知                    │
├──────────────────────────────────────────────────────────────┤
│ 场景 2: Provider + Agent 注册 (8min)                          │
│   LLM Provider → ChatClient Agent → Team Agent               │
├──────────────────────────────────────────────────────────────┤
│ 场景 3: 工具网关 + 绑定 (6min)                                 │
│   创建工具 → 绑定到 Agent → 展示统一工具管理                     │
├──────────────────────────────────────────────────────────────┤
│ 场景 4: 实时对话 (8min)                                       │
│   单 Agent Chat → Team Agent 多 Agent 协作                    │
├──────────────────────────────────────────────────────────────┤
│ 场景 5: 工作流编排 (8min)                                      │
│   DAG 设计 → 执行 → SignalR 实时追踪                           │
├──────────────────────────────────────────────────────────────┤
│ 场景 6: 告警闭环 ⭐ (10min)                                    │
│   数据源 → 告警规则 → SOP → 触发告警 → 自动响应 → 复盘          │
└──────────────────────────────────────────────────────────────┘
```

---

## 五、下一步工作

### 5.1 需要开发的 Demo 增强

| 编号 | 任务 | 优先级 | 说明 |
|------|------|--------|------|
| D-1 | ~~`seed-demo-data.ps1` 数据预注册脚本~~ | **Done** | `demo/seed-demo-data.ps1` — 注册 Provider、Agent、Tool、SOP、绑定关系 |
| D-2 | ~~告警 Webhook 触发便捷脚本~~ | **Done** | `demo/fire-alert.ps1 -Type HighErrorRate` |
| D-3 | ~~SOP 业务背景参考文件~~ | **Done** | `demo/sop-files/` — architecture.md、observability-queries.md、troubleshooting-guide.md |
| D-4 | Grafana Dashboard 部署 | P1 | 运行 `grafana-dashboards.ps1` 提供可视化仪表盘作为辅助展示 |
| D-5 | 演示录屏 Plan B | P2 | 预录核心场景视频，LLM 调用不稳定时作为备份 |
| D-6 | 故障注入开关 | P2 | 在 demo-app 中增加 `/chaos` 端点，手动注入 500 错误或延迟 |

### 5.2 推荐演示顺序

- **10 分钟快速演示**: 场景 1 + 场景 6（概览 + 核心闭环）
- **25 分钟标准演示**: 场景 1 + 场景 2 + 场景 4 + 场景 6
- **50 分钟完整演示**: 全部 6 个场景按顺序
