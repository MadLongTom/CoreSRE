# CoreSRE — 未完成 SPEC 汇总与执行计划

**文档编号**: INCOMPLETE-SPEC-INDEX  
**版本**: 1.0.0  
**创建日期**: 2026-02-15  
**关联文档**: [SPEC-INDEX](SPEC-INDEX.md) | [WORKFLOW-UPGRADE-SPEC-INDEX](WORKFLOW-UPGRADE-SPEC-INDEX.md) | [Alert-Incident-Response-SPEC-INDEX](Alert-Incident-Response-SPEC-INDEX.md)  

> 本文档汇总所有尚未实现的 SPEC，按优先级分组，附带依赖关系分析和推荐执行计划。  
> 已完成的 SPEC（001, 004-021, 080-082, 090-094, 100-103, 200-204）不在此列。  
> 原 SPEC-INDEX 中的 022-026（工作流高级特性）已被 SPEC-080~085 + SPEC-100~103 替代。  
> 原 SPEC-INDEX 中的 030-033（AIOps）已被 SPEC-110~118（Alert Incident Response）替代。

---

## 一、编号体系说明

| 编号范围 | 模块 | 来源文档 | 状态 |
|----------|------|---------|------|
| 000 | M0: Aspire AppHost | SPEC-INDEX | ✅ 已完成 |
| 001-004 | M1: Agent Registry | SPEC-INDEX | ✅ 已完成（除 002, 003） |
| 010-015 | M2: Tool Gateway | SPEC-INDEX | ✅ 已完成（除 014, 015） |
| 020-021 | M3: Workflow Engine | SPEC-INDEX | ✅ 已完成 |
| 022-026 | M3: 工作流高级特性（原） | SPEC-INDEX | ⚠️ 已被替代（见下方） |
| 030-033 | M4: AIOps（原） | SPEC-INDEX | ⚠️ 已被替代（见下方） |
| 040-041 | M5: Observability | SPEC-INDEX | ✅ 040 完成，❌ 041 |
| 049-052 | M6: Security | SPEC-INDEX | ❌ 未完成 |
| 059-065 | M7: Frontend | SPEC-INDEX | ❌ 大部分未完成 |
| 080-085 | 工作流引擎升级 | WORKFLOW-UPGRADE-SPEC-INDEX | ✅ 080-083 完成，❌ 084-085 |
| 090-094 | Agent Skills & 沙箱 | SKILLS-SANDBOX-SPEC-INDEX | ✅ 全部完成 |
| 100-103 | Team Agent | TEAM-AGENT-SPEC-INDEX | ✅ 全部完成 |
| 110-118 | Alert Incident Response | Alert-Incident-Response-SPEC-INDEX | ❌ 全部未完成 |
| 200-204 | SRE 数据源 | DATASOURCE-SPEC-INDEX | ✅ 全部完成 |

---

## 二、已替代 SPEC 对照表

以下 SPEC 在原 SPEC-INDEX 中定义，但已被后续更详细的设计取代，**不再需要实现**：

| 原编号 | 原标题 | 替代方 | 说明 |
|--------|--------|--------|------|
| SPEC-022 | Agent Handoff 编排 | SPEC-101 (Team Agent 执行引擎) | Handoff 作为 Team Agent 的 RoundRobin/Selector 模式实现 |
| SPEC-023 | Group Chat 多 Agent 协商 | SPEC-101 (Team Agent 执行引擎) | GroupChat 作为 Team Agent 的 MagneticOne 模式实现 |
| SPEC-024 | 工作流执行暂停/取消/回溯 | 部分由 SPEC-084 覆盖 | 暂停/回溯改为 P2，依赖新执行栈引擎 |
| SPEC-025 | 短时/长时工具统一编排 | 留为 P2 独立 SPEC | 未被替代，仍为有效 P2 |
| SPEC-026 | 工作流发布为 WorkflowAgent | SPEC-102/103 + 现有实现 | WorkflowAgent 发布已在 021 中实现 |
| SPEC-030 | 告警事件接收与聚合 | SPEC-110 + SPEC-112 | 领域模型 + Webhook 路由 |
| SPEC-031 | LLM 驱动根因分析 | SPEC-114 | RCA Team Agent 方案 |
| SPEC-032 | 修复操作与人工审批 | SPEC-113 | SOP 自动执行链路 |
| SPEC-033 | AIOps 端到端工作流 | SPEC-110~118 整体 | 全新的 Alert Incident Response 架构 |

---

## 三、未完成 SPEC 清单（按优先级）

### P0 — 前置基础设施（阻塞多个下游功能）

| SPEC | 标题 | 来源 | 依赖 | 预估工作量 | 阻塞的下游 |
|------|------|------|------|-----------|-----------|
| **SPEC-049** | 身份认证与 RBAC（JWT + 角色权限） | SPEC-INDEX M6 | 无 | 3-4 天 | SPEC-051 审批流, SPEC-059 登录页 |
| **SPEC-059** | 登录页面与认证状态管理 | SPEC-INDEX M7 | SPEC-049 | 2-3 天 | 所有需登录的前端页面 |

**P0 说明**: JWT 认证是安全审批、操作审计的前置。登录页是前端可交付的门户。目前系统已有基本路由和 API 框架但无认证，属于技术债。

---

### P1 — 核心功能（MVP 关键路径）

#### P1-A: Alert Incident Response 核心链路

| SPEC | 标题 | 来源 | 依赖 | 预估工作量 |
|------|------|------|------|-----------|
| **SPEC-110** | 告警事故领域模型 | Alert-SPEC-INDEX | 无 | 2-3 天 |
| **SPEC-111** | AlertRule CRUD | Alert-SPEC-INDEX | SPEC-110 | 2-3 天 |
| **SPEC-112** | Webhook 告警路由改造 | Alert-SPEC-INDEX | SPEC-111 | 3-4 天 |
| **SPEC-113** | 链路 A — SOP 自动执行 | Alert-SPEC-INDEX | SPEC-112 | 4-5 天 |
| **SPEC-114** | 链路 B — 根因分析 Team | Alert-SPEC-INDEX | SPEC-113 | 4-5 天 |
| **SPEC-116** | 前端：Incident 实时推送 + 详情页 | Alert-SPEC-INDEX | SPEC-113 | 4-5 天 |

**P1-A 说明**: Alert Incident Response 是产品核心差异化功能（课题成果 4），链路 110→111→112→113 是最小可演示路径。SPEC-114（RCA Team）利用已完成的 Team Agent 基础设施（SPEC-101~103）。

#### P1-B: 工作流引擎补全

| SPEC | 标题 | 来源 | 依赖 | 预估工作量 |
|------|------|------|------|-----------|
| ~~SPEC-083~~ | ~~表达式引擎与错误处理~~ | WORKFLOW-UPGRADE-SPEC-INDEX | SPEC-081 ✅ | ✅ 已完成 |

**P1-B 说明**: ~~SPEC-083 是工作流引擎升级的最后一块 P1 拼图。~~ ✅ 已完成。条件默认分支（else）、节点级错误策略（onError: stop/continueWithEmpty/continueWithError）、节点重试机制（maxRetries + 线性退避）均已实现。

#### P1-C: 可观测性

| SPEC | 标题 | 来源 | 依赖 | 预估工作量 |
|------|------|------|------|-----------|
| ~~SPEC-040~~ | ~~Agent 调用全链路追踪~~ | SPEC-INDEX M5 | 无（OTel 基础已就绪） | ✅ 已完成 |

**P1-C 说明**: ~~Aspire Dashboard + OTel 基础设施已在 SPEC-000 中配置。~~ ✅ 已完成。CoreSRETelemetry ActivitySource 注册，工作流/节点/Agent/LLM/Tool 全链路 Span 层级，TraceId 持久化。

#### P1-D: 安全与治理

| SPEC | 标题 | 来源 | 依赖 | 预估工作量 |
|------|------|------|------|-----------|
| **SPEC-050** | Agent 工具访问权限控制 | SPEC-INDEX M6 | 无 | 2-3 天 |
| **SPEC-051** | 危险操作审批流 | SPEC-INDEX M6 | SPEC-049 (JWT) | 3-4 天 |
| **SPEC-052** | 操作审计日志查询 | SPEC-INDEX M6 | 无 | 2-3 天 |

**P1-D 说明**: SPEC-050 和 SPEC-052 无前置依赖可立即开始。SPEC-051 审批流依赖 JWT 认证（SPEC-049）。

#### P1-E: 前端核心页面

| SPEC | 标题 | 来源 | 依赖 | 预估工作量 |
|------|------|------|------|-----------|
| **SPEC-060** | 系统 Dashboard 与布局框架 | SPEC-INDEX M7 | 无 | 2-3 天 |
| **SPEC-065** | AIOps 告警与修复页面 | SPEC-INDEX M7 | SPEC-113 (告警后端) | 3-4 天 |

**P1-E 说明**: SPEC-060 是系统入口页面，可随时开发。SPEC-065 改为对接新的 Alert Incident Response API（替代原 030-033），依赖 SPEC-113 后端可用。

---

### P2 — 增强功能（第二轮迭代）

| SPEC | 标题 | 来源 | 依赖 | 预估工作量 |
|------|------|------|------|-----------|
| **SPEC-002** | Agent 健康检查与状态管理 | SPEC-INDEX M1 | SPEC-001 ✅ | 2-3 天 |
| **SPEC-014** | 工具配额管理与熔断 | SPEC-INDEX M2 | SPEC-013 ✅ | 2-3 天 |
| **SPEC-015** | 工具调用审计日志 | SPEC-INDEX M2 | SPEC-013 ✅ | 2-3 天 |
| **SPEC-025** | 短时/长时工具统一编排 | SPEC-INDEX M3 | SPEC-021 ✅ | 3-4 天 |
| **SPEC-041** | Agent 状态可视化面板 | SPEC-INDEX M5 | SPEC-002, SPEC-040 | 3-4 天 |
| **SPEC-084** | 部分执行与数据追踪 | WORKFLOW-UPGRADE-SPEC-INDEX | SPEC-081 ✅ | 4-5 天 |
| **SPEC-085** | 前端升级与并发执行 | WORKFLOW-UPGRADE-SPEC-INDEX | SPEC-082 ✅ | 4-5 天 |
| **SPEC-115** | 链路 C — SOP 自动生成 | Alert-SPEC-INDEX | SPEC-114 | 4-5 天 |
| **SPEC-117** | 前端：AlertRule 管理 + 执行看板 | Alert-SPEC-INDEX | SPEC-116 | 3-4 天 |

---

### P3 — 远期增强

| SPEC | 标题 | 来源 | 依赖 | 预估工作量 |
|------|------|------|------|-----------|
| **SPEC-003** | Agent 能力语义搜索 | SPEC-INDEX M1 | SPEC-001 ✅ | 3-4 天 |
| **SPEC-118** | 通知渠道集成（预留） | Alert-SPEC-INDEX | SPEC-117 | 2-3 天 |

---

## 四、依赖关系图

```
                    ┌──────────┐
                    │ SPEC-049 │ (JWT 认证) P0
                    │  3-4d    │
                    └────┬─────┘
                         │
              ┌──────────┼──────────┐
              ▼          ▼          ▼
        ┌──────────┐ ┌──────────┐ ┌──────────┐
        │ SPEC-059 │ │ SPEC-051 │ │ SPEC-050 │ (无依赖)
        │ 登录页面  │ │ 审批流   │ │ 工具权限  │
        │  2-3d    │ │  3-4d    │ │  2-3d    │
        └──────────┘ └──────────┘ └──────────┘


  ┌──────────┐     ┌──────────┐     ┌──────────┐     ┌──────────┐
  │ SPEC-110 │────▶│ SPEC-111 │────▶│ SPEC-112 │────▶│ SPEC-113 │
  │ 领域模型  │     │ CRUD     │     │ Webhook  │     │ SOP执行  │
  │  2-3d    │     │  2-3d    │     │  3-4d    │     │  4-5d    │
  └──────────┘     └──────────┘     └──────────┘     └────┬─────┘
                                                          │
                                          ┌───────────────┼───────────────┐
                                          ▼               ▼               ▼
                                    ┌──────────┐   ┌──────────┐   ┌──────────┐
                                    │ SPEC-114 │   │ SPEC-116 │   │ SPEC-065 │
                                    │ RCA Team │   │ Incident │   │ AIOps页面│
                                    │  4-5d    │   │ UI 4-5d  │   │  3-4d    │
                                    └────┬─────┘   └────┬─────┘   └──────────┘
                                         ▼               ▼
                                   ┌──────────┐   ┌──────────┐
                                   │ SPEC-115 │   │ SPEC-117 │
                                   │ SOP生成  │   │ Rule UI  │
                                   │ P2 4-5d  │   │ P2 3-4d  │
                                   └──────────┘   └──────────┘


  SPEC-081 ✅ ──────▶ ┌──────────┐
                      │ SPEC-083 │ (表达式引擎)
                      │  5-7d    │
                      └──────────┘

  独立可启动（无前置依赖）:
    SPEC-040 (全链路追踪)    3-4d
    SPEC-050 (工具权限)      2-3d
    SPEC-052 (审计日志)      2-3d
    SPEC-060 (Dashboard)     2-3d
```

---

## 五、工作量统计

| 优先级 | SPEC 数量 | 合计预估工作量 |
|--------|----------|---------------|
| P0 | 2 | 5-7 天 |
| P1 | 11 | 36-48 天 |
| P2 | 9 | 28-36 天 |
| P3 | 2 | 5-7 天 |
| **合计** | **24** | **74-98 天** |

---

## 六、推荐执行计划

> 策略：**双轨并行**（Alert 链路 + 安全/基础设施），保持每个 Sprint 交付可演示切片。  
> 估算基于单人全职开发，双人可压缩 ~40%。

### Sprint 7（第 1-2 周）— 安全基础 + Alert 领域模型

**目标**: 补齐认证体系 + 建立 Alert 数据基础

| 并行轨道 | SPEC | 工作量 | 产出 |
|----------|------|--------|------|
| 轨道 A: 安全基础 | **SPEC-049** (JWT 认证) | 3-4d | JWT 登录 API + RBAC 中间件 |
| 轨道 A: 安全基础 | **SPEC-059** (登录页面) | 2-3d | 前端 Login 页 + Token 管理 |
| 轨道 B: Alert 基础 | **SPEC-110** (领域模型) | 2-3d | AlertRule/Incident/SOP 实体 + EF 迁移 |
| 轨道 B: Alert 基础 | **SPEC-111** (AlertRule CRUD) | 2-3d | AlertRule 的完整 CRUD API |

**演示切片**: 用户登录系统 → 创建 AlertRule → 查看 AlertRule 列表

---

### Sprint 8（第 3-4 周）— Alert 核心链路 + 表达式引擎

**目标**: 打通告警接收到 SOP 执行的完整链路

| 并行轨道 | SPEC | 工作量 | 产出 |
|----------|------|--------|------|
| 轨道 A: Alert 链路 | **SPEC-112** (Webhook 路由) | 3-4d | Alertmanager Webhook → AlertRule 匹配 → Incident 创建 |
| 轨道 A: Alert 链路 | **SPEC-113** (SOP 自动执行) | 4-5d | IncidentDispatcher → ResponderAgent → SOP 执行 |
| 轨道 B: 工作流补全 | **SPEC-083** (表达式引擎) | 5-7d | Jint 表达式引擎 + ErrorPolicy + Config 解析 |

**演示切片**: Alertmanager 推送告警 → 自动匹配规则 → 创建 Incident → 执行 SOP → 工作流条件分支使用复杂表达式

---

### Sprint 9（第 5-6 周）— RCA + 前端 + 可观测性

**目标**: 补齐根因分析链路 + Incident 前端实时体验

| 并行轨道 | SPEC | 工作量 | 产出 |
|----------|------|--------|------|
| 轨道 A: Alert 深化 | **SPEC-114** (RCA Team) | 4-5d | MetricAnalyst + LogAnalyst + K8sExpert Team 协作 |
| 轨道 A: Alert 深化 | **SPEC-116** (Incident 实时 UI) | 4-5d | IncidentHub SignalR + 事件流 + 详情页 |
| 轨道 B: 基础设施 | **SPEC-040** (全链路追踪) | 3-4d | OTel Span 层级 + Aspire Dashboard 可视化 |
| 轨道 B: 基础设施 | **SPEC-060** (Dashboard) | 2-3d | 系统概览首页 + 布局框架 |

**演示切片**: 告警触发 → 多 Agent RCA 协作 → 前端实时看到 Incident 进展 → Aspire 全链路追踪

---

### Sprint 10（第 7-8 周）— 安全治理 + AIOps 前端

**目标**: 补齐安全模块 + Alert 前端完整度

| 并行轨道 | SPEC | 工作量 | 产出 |
|----------|------|--------|------|
| 轨道 A: 安全治理 | **SPEC-050** (工具权限) | 2-3d | Agent↔Tool 权限白名单 |
| 轨道 A: 安全治理 | **SPEC-051** (审批流) | 3-4d | destructive 工具审批拦截 |
| 轨道 A: 安全治理 | **SPEC-052** (审计日志) | 2-3d | 操作审计 API + 查询 |
| 轨道 B: Alert 前端 | **SPEC-065** (AIOps 告警页) | 3-4d | Incident 列表/详情/审批操作页 |

**演示切片**: 危险工具调用 → 自动阻断等待审批 → 管理员审批 → 执行 + 审计留痕

---

### Sprint 11+（P2 迭代）— 按需选取

P2 SPEC 按业务价值自由排列，推荐顺序：

1. **SPEC-002** (Agent 健康检查) — 运维必需，基于已有 Agent 基础快速实现
2. **SPEC-084** (部分执行与数据追踪) — 工作流调试体验提升
3. **SPEC-085** (前端升级与并发执行) — 工作流性能瓶颈解决
4. **SPEC-115** (SOP 自动生成) — Alert 智能化关键特性
5. **SPEC-117** (AlertRule 管理 UI) — Alert 前端完整度
6. **SPEC-014** (工具配额熔断) — 生产稳定性
7. **SPEC-015** (工具调用审计) — 合规需求
8. **SPEC-025** (短时/长时工具编排) — 高级编排能力
9. **SPEC-041** (Agent 状态面板) — 可视化增强

---

## 七、风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| SPEC-083 表达式引擎复杂度高 | 可能延期 1-2 天 | Jint 库成熟度高，优先实现核心表达式子集 |
| SPEC-114 RCA Team 需要真实数据源 | LLM 分析质量依赖数据 | 已有 SPEC-200~204 数据源基础，可用 Mock 数据先行 |
| JWT 认证改造涉及全局中间件 | 可能影响已有 API | 渐进式添加 `[Authorize]`，先不强制全局 |
| Alert SignalR Hub 与现有 WorkflowHub 共存 | 端口/连接管理 | 同一 SignalR Server 多 Hub，已有成熟模式 |

---

## 八、关键里程碑

| 里程碑 | 时间点 | 标志性产出 |
|--------|--------|-----------|
| **M1: 安全可用** | Sprint 7 结束 | JWT 登录可用，前端有认证保护 |
| **M2: Alert MVP** | Sprint 8 结束 | 告警 → 规则匹配 → SOP 执行全链路跑通 |
| **M3: 智能闭环** | Sprint 9 结束 | RCA 多 Agent 协作 + Incident 前端实时推送 |
| **M4: 安全合规** | Sprint 10 结束 | 权限+审批+审计三件套完成 |
| **M5: 产品完善** | Sprint 11+ | P2 增强功能按需迭代 |
