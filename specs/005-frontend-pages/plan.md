# Implementation Plan: 前端管理页面（Agent Registry + 搜索）

**Branch**: `005-frontend-pages` | **Date**: 2026-02-10 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/005-frontend-pages/spec.md`

## Summary

为 CoreSRE 平台构建前端管理页面，覆盖 Agent CRUD 全生命周期（列表、注册、详情/编辑、删除）和技能搜索。使用现有 React 19 + Vite 7 + shadcn/ui 技术栈，新增 React Router 路由和统一布局框架。前端通过 Vite proxy 调用后端 REST API（`/api/agents`、`/api/agents/search`），对应 SPEC-001 和 SPEC-003 的后端实现。SPEC-000（基础设施）和 SPEC-004（内部 Framework 集成）无前端页面需求。

## Technical Context

**Language/Version**: TypeScript ~5.9.3 / React 19.2  
**Primary Dependencies**: React Router（路由）、shadcn/ui（组件库）、Tailwind CSS v4（样式）、lucide-react（图标）、radix-ui（无障碍基础）  
**Storage**: N/A（前端无本地持久化，所有数据通过 REST API 获取）  
**Testing**: 不在本 Spec 范围内（spec.md 未要求测试，且 Constitution TDD 原则主要针对后端 Domain/Application 层）  
**Target Platform**: 桌面浏览器（≥ 1280px 宽度），Chrome/Edge/Firefox 现代版本  
**Project Type**: Web application — Frontend SPA（已有项目，扩展功能）  
**Performance Goals**: 列表页首次渲染 < 2s，筛选交互 < 500ms，搜索从停止输入到结果展示 < 2s  
**Constraints**: 不实现分页、认证、国际化；Agent 数量 < 100  
**Scale/Scope**: 4 个页面（列表、注册、详情/编辑、搜索）+ 1 个布局框架 + 1 个 404 页面

## Constitution Check (Pre-Design)

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Spec-Driven Development | ✅ PASS | spec.md 已创建并通过 16/16 checklist 验证 |
| II | Test-Driven Development | ⚠️ 特殊 | Constitution TDD 原则针对后端 DDD 四层。前端 SPA 无 Domain/Application 层分层。spec.md 未要求前端测试。后续可通过 Playwright/Vitest 补充，但本 Spec 不强制 |
| III | Domain-Driven Design | ⚠️ 不适用 | 前端无 Domain/Application/Infrastructure 分层。前端遵循组件化架构（pages → components → lib/api）。DDD 原则适用于后端，前端遵循 Constitution Frontend naming conventions |
| IV | Test Immutability | ✅ PASS | 无已提交前端测试受影响 |
| V | Interface-Before-Implementation | ⚠️ 特殊 | TypeScript types/interfaces 定义在 `types/` 目录中，等同于 Interface-First 原则。但前端不使用 DI 容器，组件直接导入 API 函数 |

**Gate Result**: ✅ PASS — Constitution 原则 I/IV 通过。原则 II/III/V 对前端 SPA 的适用性受限（Constitution 明确定义了后端 DDD 四层结构），以合理方式落地：TypeScript 类型先行、组件化分层、API 层隔离。

## Project Structure

### Documentation (this feature)

```text
specs/005-frontend-pages/
├── plan.md              # This file
├── research.md          # Phase 0: 技术研究
├── data-model.md        # Phase 1: 前端类型定义设计
├── quickstart.md        # Phase 1: 快速验证步骤
├── contracts/           # Phase 1: API 消费契约（引用 SPEC-002 OpenAPI）
└── tasks.md             # Phase 2 output (by /speckit.tasks)
```

### Source Code (repository root)

```text
Frontend/
├── src/
│   ├── main.tsx                          # 入口（已有）
│   ├── App.tsx                           # 修改：路由配置
│   ├── index.css                         # 已有，无修改
│   ├── types/
│   │   └── agent.ts                      # 新增：TypeScript 类型定义
│   ├── lib/
│   │   ├── utils.ts                      # 已有，无修改
│   │   └── api/
│   │       └── agents.ts                 # 新增：Agent API 客户端函数
│   ├── components/
│   │   ├── ui/                           # 已有 shadcn/ui 组件 + 新增组件
│   │   │   ├── button.tsx                # 已有
│   │   │   ├── card.tsx                  # 已有
│   │   │   ├── badge.tsx                 # 新增（shadcn add）
│   │   │   ├── dialog.tsx               # 新增（shadcn add）
│   │   │   ├── input.tsx                # 新增（shadcn add）
│   │   │   ├── label.tsx                # 新增（shadcn add）
│   │   │   ├── select.tsx               # 新增（shadcn add）
│   │   │   ├── separator.tsx            # 新增（shadcn add）
│   │   │   ├── table.tsx                # 新增（shadcn add）
│   │   │   └── textarea.tsx             # 新增（shadcn add）
│   │   ├── layout/
│   │   │   ├── AppLayout.tsx             # 新增：统一布局（侧边栏 + 主内容区）
│   │   │   └── Sidebar.tsx               # 新增：侧边栏导航
│   │   └── agents/
│   │       ├── AgentTypeBadge.tsx         # 新增：类型颜色标签
│   │       ├── AgentStatusBadge.tsx       # 新增：状态标签
│   │       ├── AgentCardSection.tsx       # 新增：AgentCard 只读展示 / 编辑区域
│   │       ├── LlmConfigSection.tsx       # 新增：LLM 配置只读展示 / 编辑区域
│   │       ├── DeleteAgentDialog.tsx      # 新增：删除确认对话框
│   │       └── SkillHighlight.tsx         # 新增：搜索结果关键词高亮
│   └── pages/
│       ├── AgentListPage.tsx             # 新增：Agent 列表（US1）
│       ├── AgentCreatePage.tsx           # 新增：Agent 注册（US2）
│       ├── AgentDetailPage.tsx           # 新增：Agent 详情/编辑（US3）
│       ├── AgentSearchPage.tsx           # 新增：Agent 搜索（US5）
│       └── NotFoundPage.tsx              # 新增：404 页面
└── package.json                          # 修改：添加 react-router 依赖
```

**Structure Decision**: 沿用现有 Frontend/src/ 结构，新增 `types/`（TypeScript 类型）、`lib/api/`（API 客户端）、`components/layout/`（布局）、`components/agents/`（Agent 业务组件）、`pages/`（页面级组件）。遵循 Constitution Frontend naming conventions（PascalCase .tsx 组件、camelCase 工具函数、`api` 前缀 API 调用）。

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| 前端无 TDD 流程 | Constitution TDD 原则定义了后端 DDD 四层（Domain.Tests, Application.Tests 等）。前端 SPA 无此分层。spec.md 未要求前端测试 | 强行在前端实施后端 TDD 流程不匹配架构。后续可通过 Vitest + Testing Library 补充组件测试 |
| 前端无 Interface-Before-Implementation | TypeScript `types/` 等同于接口定义。前端不使用 DI 容器，直接导入模块 | 引入 DI 框架（如 InversifyJS）过度工程化，与 React 生态不匹配 |

## Constitution Check (Post-Design)

*Re-evaluation after Phase 1 design completion.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Spec-Driven Development | ✅ PASS | Plan 所有设计决策均可追溯到 spec.md 的 FR/US。data-model.md 类型与 spec.md AC 一致。contracts/ 覆盖 spec.md 定义的所有 API 交互 |
| II | Test-Driven Development | ⚠️ N/A | 前端 SPA 不适用后端 TDD 四层流程（已在 Complexity Tracking 记录）。TypeScript 编译器提供编译时类型检查。Zod schema 提供运行时验证 |
| III | Domain-Driven Design | ⚠️ N/A | 前端采用组件化分层（types → lib/api → components → pages），等效于职责分离。不适用 DDD 四层（已在 Complexity Tracking 记录） |
| IV | Test Immutability | ✅ PASS | 无已提交测试受影响 |
| V | Interface-Before-Implementation | ✅ PASS | TypeScript types (`types/agent.ts`) 和 API contract (`contracts/api-contract.md`) 在实现前定义。data-model.md 完整记录了所有类型和映射关系 |

**Gate Result**: ✅ PASS — Post-design 验证通过。所有可适用的 Constitution 原则满足，不适用原则已合理记录。

## Generated Artifacts

| File | Phase | Description |
|------|-------|-------------|
| `plan.md` | — | 本文件 |
| `research.md` | Phase 0 | 5 项技术决策（路由、API 客户端、表单、类型策略、状态管理） |
| `data-model.md` | Phase 1 | 14 个 TypeScript 类型定义，映射后端 DTO |
| `contracts/api-contract.md` | Phase 1 | 6 个 REST API 端点契约 |
| `quickstart.md` | Phase 1 | 28 项功能验证清单 |
