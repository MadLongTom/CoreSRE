# Research: 005-frontend-pages

## R1: Routing Library

**Decision**: React Router v7（Declarative 模式）

**Rationale**:
- 项目仅 4-5 个路由，无 SSR 需求，Declarative 模式（`BrowserRouter` + `Routes` + `Route`）最简
- React 19 一等支持（v7 是 Remix 继承者，与 React 19 同步设计）
- 行业标准（npm 周下载 12M+），shadcn/ui 生态默认使用 React Router
- 无需 Vite 插件或代码生成，安装即用
- 升级路径：Declarative → Data → Framework 模式渐进升级

**Alternatives Considered**:
| 方案 | 拒绝原因 |
|------|----------|
| TanStack Router | TypeScript 类型推断优秀，但需 Vite 插件 + route tree codegen，4-5 个路由过度工程化 |
| Wouter | 超轻量（~1.5 KB），但缺少嵌套布局、数据加载，升级路径不足 |

**Installation**: `npm install react-router`

---

## R2: API Client Approach

**Decision**: Plain `fetch` wrapper（自定义类型化函数）

**Rationale**:
- 6 个端点、<100 项数据 — 无需分页、轮询、无限滚动或乐观更新
- 零额外依赖 — `fetch` 是浏览器原生 API，TypeScript `lib.dom.d.ts` 内置类型
- React 19 方向一致 — React 19 的 `use()` hook 和 Server Components 朝原生 fetch 集成演进
- Vite proxy 已配置 — 相对 URL（`/api/agents`）开箱即用
- 完全类型安全 — 单个 `agents.ts` 文件包含所有类型化函数

**Alternatives Considered**:
| 方案 | 拒绝原因 |
|------|----------|
| axios | 增加 ~13 KB 依赖，主要优势（拦截器、自动 JSON、请求取消）均可由 fetch + AbortController 平替 |
| TanStack Query | 为复杂缓存场景设计（stale-while-revalidate、后台刷新），简单 CRUD <100 项过度工程化 |
| SWR | 类似 TanStack Query 的论点，功能更少，性价比低于 TanStack Query |

**Installation**: 无需安装

**Usage Pattern**:
```typescript
// src/lib/api/agents.ts
async function getAgents(type?: string): Promise<Result<AgentSummaryDto[]>> {
  const url = type ? `/api/agents?type=${encodeURIComponent(type)}` : "/api/agents";
  const res = await fetch(url);
  if (!res.ok) throw new ApiError(res.status, await res.json());
  return res.json();
}
```

**Reconsider Trigger**: 当项目需要实时轮询、乐观更新、无限滚动或 >3 个组件共享同一远程数据时，升级到 TanStack Query。

---

## R3: Form Handling

**Decision**: React Hook Form + Zod（通过 shadcn/ui `<Form>` 组件集成）

**Rationale**:
- shadcn/ui 的 `<Form>` 组件原生构建于 React Hook Form + Zod — 这是官方模式，不是第三方集成
- Zod schema → `z.infer<T>` 实现类型 + 验证单一来源
- 支持 `discriminatedUnion` 映射后端 AgentType 条件字段（A2A → AgentCard, ChatClient → LlmConfig, Workflow → WorkflowRef）
- 编辑表单开箱即用 — `useForm({ defaultValues: existingAgent })` 预填所有字段
- RHF ref-based 最小化重渲染（vs useState 每次按键全表单重渲染）
- 总计 ~12 KB gzipped（RHF 8.5KB + resolvers 1KB + Zod 2.5KB）
- React 19 完全兼容（RHF v7.54+）

**Alternatives Considered**:
| 方案 | 拒绝原因 |
|------|----------|
| useState 受控组件 | 3 个字段可行，但 ~8 字段 + 条件嵌套对象 + 验证 → 手动重造 RHF。缺少 isDirty/isSubmitting/字段级错误跟踪 |
| Formik | 2021 年后未有大版本更新，实质弃维。Bundle ~44KB。无 shadcn/ui 集成。TypeScript 推断弱 |
| TanStack Form | v0.x 阶段，社区小，无 shadcn/ui 集成。生产项目不适合 |

**Installation**: `npx shadcn@latest add form input select textarea label`（同时安装 `react-hook-form`、`@hookform/resolvers`、`zod` 及 shadcn 组件）

---

## R4: Frontend Type Strategy

**Decision**: 手动定义 TypeScript 类型，映射后端 DTO 形状

**Rationale**:
- 后端使用 C# record（非 OpenAPI 生成），暂无 OpenAPI 文档输出
- 6 个 DTO + 4 个嵌套类型 — 数量少，手动维护成本低
- 类型定义集中于 `src/types/agent.ts`，单文件 < 100 行
- 命名保持与后端 DTO 一致（去掉 `Dto` 后缀），便于对照

**Alternatives Considered**:
| 方案 | 拒绝原因 |
|------|----------|
| OpenAPI 代码生成 | 后端未暴露 OpenAPI 文档。Swashbuckle/NSwag 集成超出本 Spec 范围 |
| Shared schema (JSON Schema) | 需要额外基础设施，10 个类型手动维护更直接 |

---

## R5: State Management

**Decision**: React 内置状态管理（`useState` + `useEffect` + prop drilling）

**Rationale**:
- 4 个页面相互独立，无跨页面共享状态需求
- 列表页有自己的数据、详情页有自己的数据，搜索页有自己的数据
- Agent 类型筛选是列表页局部状态（URL query param 或 useState）
- 路由参数（agentId）通过 React Router `useParams()` 获取
- 无需全局 store 的典型场景

**Alternatives Considered**:
| 方案 | 拒绝原因 |
|------|----------|
| Zustand | 轻量全局状态库，但本项目无跨页面共享状态需求 |
| Redux / Redux Toolkit | 过度工程化，CRUD 管理页面不需要复杂状态管理 |
| Jotai / Recoil | 原子化状态，适合复杂组件树，本项目页面间独立 |

**Reconsider Trigger**: 当出现跨页面状态共享需求（如全局通知、用户偏好）时，升级到 Zustand。
