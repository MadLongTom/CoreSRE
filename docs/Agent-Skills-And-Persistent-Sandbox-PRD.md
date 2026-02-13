# CoreSRE — Agent Skills 与持久化沙箱 产品需求文档

**文档编号**: PRD-002  
**版本**: 1.0.0  
**状态**: DRAFT  
**创建日期**: 2026-02-13  
**关联文档**: [PRD-001](PRD.md) | [OpenClaw 调研报告](OpenClaw-Skills-Research.md) | [SPEC-INDEX](specs/SPEC-INDEX.md)

---

## 1. 背景与动机

### 1.1 当前痛点

CoreSRE 现有 Agent 系统存在三个核心问题：

| 痛点 | 现状 | 影响 |
|------|------|------|
| **知识不可复用** | Agent 的领域知识硬编码在 `SystemPrompt` 单一字符串中 | 无法跨 Agent 共享知识模块，重复维护成本高 |
| **沙箱即用即弃** | K8s Pod 按 `agentId/conversationId` 创建，对话结束即销毁 | 用户安装的工具、配置的环境、产生的文件全部丢失 |
| **无文件系统** | Agent/Skill 的附属文件（脚本、参考文档、资产）无处存储 | 无法实现 Skill 文件包的上传、分发和沙箱挂载 |

### 1.2 调研依据

基于 [OpenClaw Agent Skills 调研报告](OpenClaw-Skills-Research.md)，我们借鉴其核心设计理念：

- **Skills = Markdown 知识模块**：不注册新工具，而是教 LLM 如何使用已有工具完成特定任务
- **渐进式披露**：SystemPrompt 仅列 Skill 名称 + 描述摘要（~100 tokens/skill），LLM 按需加载完整内容
- **文件包结构**：`SKILL.md` + 可选的 `scripts/`、`references/`、`assets/` 子目录

### 1.3 系统扩展点

| 已有基础设施 | 可利用方式 |
|-------------|-----------|
| **MinIO (S3)** — AppHost 已配置，持久化卷 `coresre-s3` | Skill 文件包存储、沙箱文件持久化、用户工作区存储 |
| **K8s 沙箱** — `SandboxPodPool` 管理 Pod 生命周期 | 升级为持久化沙箱，Skill 启用时将 S3 文件挂载到沙箱 |
| **5 种 SandboxType** — SimpleBox → ComputerBox | 持久化沙箱复用同一类型体系 |
| **6 个 AIFunction 工具** — `run_command` 等 | 持久化沙箱中复用同一套工具 |

---

## 2. 产品目标

### 2.1 一句话描述

> 为 CoreSRE 的 Agent 提供**模块化知识注入**（Skills）和**有状态执行环境**（持久化沙箱），使 Agent 成为真正可定制、可持久化的智能体。

### 2.2 目标分解

| 目标 | 描述 | 衡量标准 |
|------|------|----------|
| **G1 — Agent Skills** | Agent 可绑定多个 Skill 知识模块，LLM 按需加载 | 可在不修改 Agent SystemPrompt 的前提下，通过绑定/解绑 Skill 改变 Agent 能力 |
| **G2 — 文件系统** | 基于 MinIO 的统一文件存储，支持 Skill 文件包和用户文件 | Skill 文件可上传、预览、下载；沙箱内文件可持久化回 S3 |
| **G3 — 持久化沙箱** | 从临时 Pod 升级为可复用的有状态沙箱 | 沙箱在对话结束后继续存在，用户下次对话可恢复同一环境 |
| **G4 — 沙箱管理** | 用户可查看、管理、执行命令到持久化沙箱 | 前端提供沙箱列表、Web Terminal、文件浏览器 |

---

## 3. 功能需求

### 3.1 模块 M9：Agent Skills（知识管理）

> 前置依赖：M9-FS（文件系统）

#### FR-M9-01: Skill 注册与 CRUD

- 用户可创建 Skill，提供：名称、描述（LLM 触发依据）、分类标签、Markdown 正文
- 描述字段需精心编写，它是 LLM 判断何时使用该 Skill 的唯一依据
- 支持 Skill 的查询列表（分页、按分类/状态过滤）、详情查看、更新、删除
- Skill 有三种作用域（Scope）：`Builtin`（内建预置）、`User`（用户自定义）、`Project`（项目级）
- Skill 有状态：`Active`（可被 Agent 引用）/ `Inactive`（存档不可用）

#### FR-M9-02: Skill 文件包管理

- 每个 Skill 可关联一个文件包（存储于 MinIO），包含可选的子目录：
  - `scripts/` — 可执行脚本（Python/Bash），沙箱内可直接运行
  - `references/` — 参考文档，供 LLM 按需读取
  - `assets/` — 模板、图片等资产文件
- 用户可通过前端上传/删除文件包中的文件
- 文件包在 S3 中的路径规范：`skills/{skillId}/scripts/xxx.py`

#### FR-M9-03: Skill 绑定到 Agent

- Agent 配置中新增 `SkillRefs`（Guid 列表），类似现有 `ToolRefs`
- 前端 Agent 编辑页增加 Skill 绑定 UI（多选列表，展示 Skill 名称 + 描述）
- 绑定/解绑 Skill 不影响 Agent 的其他配置

#### FR-M9-04: Skill 渐进式注入 SystemPrompt

- Agent 运行时，加载其绑定的所有 Active Skill
- 将 Skill 的 name + description 摘要列表拼接到 SystemPrompt 尾部
- 自动注入一个 `read_skill` AIFunction 工具，LLM 可调用该工具加载 Skill 完整 Markdown 正文
- 如果 Skill 有文件包，注入 `read_skill_file` AIFunction 工具，LLM 可读取 scripts/references 中的文件内容

#### FR-M9-05: Skill 工具依赖门控

- Skill 可声明依赖的工具 ID 列表（`RequiresTools`）
- Agent 绑定 Skill 时，系统验证 Agent 的 `ToolRefs` 是否覆盖 Skill 所需工具
- 不满足依赖时给出警告提示（不硬性阻止，仅警告）

#### FR-M9-06: 内建 Skills 种子数据

- 系统预置一批 SRE 领域的 Builtin Skills：
  - `incident-response` — 事件响应流程
  - `code-review` — 代码审查最佳实践
  - `database-ops` — 数据库操作指南（备份/还原/优化）
  - `monitoring-analysis` — 监控告警分析方法论
  - `api-integration` — REST/GraphQL API 集成模式

---

### 3.2 模块 M9-FS：文件系统（S3 统一存储）

> 前置依赖：AppHost MinIO 配置（已完成）

#### FR-M9FS-01: S3 存储服务抽象

- 实现 `IFileStorageService` 接口，封装 MinIO/S3 操作
- 操作：`UploadAsync`、`DownloadAsync`、`DeleteAsync`、`ListAsync`、`ExistsAsync`、`GetPresignedUrlAsync`
- 所有文件操作通过此服务，不直接依赖 MinIO SDK

#### FR-M9FS-02: Bucket 与路径规范

- 系统使用以下 Bucket 结构：

```
coresre-skills/          ← Skill 文件包
  {skillId}/
    scripts/
    references/
    assets/

coresre-sandboxes/       ← 沙箱持久化文件
  {sandboxId}/
    home/                ← 用户工作目录
    workspace/           ← 项目工作区

coresre-uploads/         ← 临时上传暂存
  {uploadId}/
```

#### FR-M9FS-03: 文件浏览 API

- 提供通用的文件浏览、上传、下载 REST API
- 支持按 Bucket + 前缀列出文件树
- 支持单文件/多文件上传（multipart）
- 支持生成预签名 URL 供前端直接下载

---

### 3.3 模块 M9-SB：持久化沙箱（Persistent Sandbox）

> 前置依赖：M9-FS（文件系统），现有 K8s 沙箱基础设施

#### FR-M9SB-01: 沙箱实体与生命周期

- 新增 `SandboxInstance` 领域实体（独立于 Agent/对话）
- 沙箱状态机：`Creating → Running → Stopped → Terminated`
- 用户可显式创建/停止/重启/删除沙箱
- 沙箱不再绑定到单个对话 — 同一沙箱可被多个对话复用
- 沙箱可独立于 Agent 存在（用户可以直接管理沙箱，不一定通过 Agent）

#### FR-M9SB-02: 沙箱配置

- 沙箱配置项（可由用户设定）：
  - `Name` — 沙箱名称（用户可读标识）
  - `SandboxType` — 复用现有 5 种类型（SimpleBox ~ ComputerBox）
  - `Image` — 容器镜像（可选覆盖默认）
  - `CpuCores` — CPU 核数（默认 1）
  - `MemoryMib` — 内存 MiB（默认 512）
  - `Namespace` — K8s 命名空间
  - `AutoStop` — 多长时间无活动后自动停止（默认 30 分钟）
  - `PersistWorkspace` — 是否将 `/workspace` 目录持久化到 S3（默认 true）

#### FR-M9SB-03: Agent 关联沙箱

- Agent 的 `LlmConfig` 扩展：
  - 现有 `EnableSandbox` → 改为 `SandboxMode`：`None`（不使用）/ `Ephemeral`（临时，保持向后兼容）/ `Persistent`（持久化）
  - 新增 `SandboxInstanceId`（可选）— 指向一个已存在的持久化沙箱
- 当 `SandboxMode=Persistent` 时：
  - 如果有 `SandboxInstanceId`，使用指定沙箱
  - 如果没有，自动创建一个持久化沙箱并绑定
- Agent 的对话开始时，如果沙箱处于 `Stopped` 状态，自动重启

#### FR-M9SB-04: Skill 文件挂载

- Agent 启用绑定了 Skill（有文件包）时：
  1. 检查 Skill 文件包是否存在于 S3（`coresre-skills/{skillId}/`）
  2. 沙箱启动/重启时，将 Skill 文件从 S3 拷贝到沙箱的 `/skills/{skillName}/` 目录
  3. LLM 可通过现有 `run_command` / `read_file` 工具访问 `/skills/` 下的文件
- 文件拷贝使用 Pod init container 或 exec 方式实现

#### FR-M9SB-05: 沙箱工作区持久化

- 沙箱内 `/workspace` 目录为用户持久化目录
- 当沙箱停止时：自动将 `/workspace` 目录同步回 S3（`coresre-sandboxes/{sandboxId}/workspace/`）
- 当沙箱重启时：从 S3 恢复 `/workspace` 到沙箱内
- 同步使用增量同步（rsync 或 tar diff），避免大文件重复传输

#### FR-M9SB-06: 沙箱命令执行

- 用户可向正在运行的沙箱发送命令并获取输出（不通过 Agent）
- API：`POST /api/sandboxes/{id}/exec`，请求体包含 `command` + `args`
- 返回 `exitCode` + `stdout` + `stderr`
- 支持 WebSocket 长连接模式（Web Terminal 场景）

#### FR-M9SB-07: 沙箱管理 API

- 完整的沙箱 CRUD API：
  - `POST /api/sandboxes` — 创建沙箱
  - `GET /api/sandboxes` — 列出沙箱（分页、按状态过滤）
  - `GET /api/sandboxes/{id}` — 沙箱详情（状态、配置、资源用量）
  - `PUT /api/sandboxes/{id}` — 更新配置
  - `DELETE /api/sandboxes/{id}` — 终止并删除
  - `POST /api/sandboxes/{id}/start` — 启动已停止的沙箱
  - `POST /api/sandboxes/{id}/stop` — 停止正在运行的沙箱
  - `POST /api/sandboxes/{id}/exec` — 执行命令

---

### 3.4 模块 M9-FE：前端页面

#### FR-M9FE-01: Skill 管理页面

- Skill 列表页：表格展示，支持按分类/状态/作用域过滤
- Skill 创建/编辑页面：
  - 基本信息表单（名称、描述、分类、作用域）
  - Markdown 编辑器（复用 Monaco Editor `language="markdown"`）
  - 文件包管理区（文件上传/删除/树状展示）
- Skill 详情页：Markdown 渲染预览 + 文件列表
- 页面路由：`/skills`、`/skills/:id`

#### FR-M9FE-02: Agent Skill 绑定 UI

- Agent 编辑页新增 Skill 绑定区域
- 展示可选 Skill 列表（名称 + 描述 + 分类标签）
- 多选绑定/解绑操作
- 依赖门控警告提示

#### FR-M9FE-03: 沙箱管理页面

- 沙箱列表页：表格展示，列显示名称、状态指示灯、类型、关联 Agent、资源用量
- 沙箱详情页：
  - 状态卡片（Running/Stopped，存活时间、CPU/Memory 用量）
  - 操作按钮：启动/停止/重启/删除
  - 配置展示（镜像、资源限制、自动停止时间）
- 页面路由：`/sandboxes`、`/sandboxes/:id`

#### FR-M9FE-04: Web Terminal

- 沙箱详情页内嵌 Web Terminal
- 基于 xterm.js + WebSocket 实现
- 用户可直接在浏览器中向沙箱执行任意命令
- 支持终端大小自适应、滚动历史

#### FR-M9FE-05: 沙箱文件浏览器

- 沙箱详情页内嵌文件浏览器
- 展示沙箱内 `/workspace` 目录的文件树
- 支持文件预览（文本文件在线查看）
- 支持文件上传（从本地到沙箱）和下载（从沙箱到本地）

---

## 4. 非功能需求

| 维度 | 要求 |
|------|------|
| **性能** | Skill 加载延迟 < 200ms；沙箱启动时间 < 30s |
| **存储** | MinIO 自动清理 90 天未使用的沙箱文件；Skill 文件包限制 100MB |
| **安全** | 沙箱非特权容器；S3 访问通过预签名 URL，不暴露凭据；沙箱网络隔离 |
| **可靠性** | 沙箱 Pod 异常退出时自动标记为 Stopped（不丢失已持久化数据） |
| **可观测性** | 沙箱生命周期事件通过 OTel Trace 追踪；S3 操作记录审计日志 |
| **向后兼容** | 现有临时沙箱（Ephemeral）行为不变，持久化为新增选项 |

---

## 5. 模块依赖关系

```
M9-FS (文件系统)         ← 前置，无依赖
    │
    ├── M9 (Agent Skills) ← 依赖 M9-FS（文件包存储）
    │
    └── M9-SB (持久化沙箱) ← 依赖 M9-FS（工作区持久化）
            │
            └── M9-FE (前端页面) ← 依赖 M9/M9-SB API
```

---

## 6. 实现路线

| Phase | 内容 | 预估工期 |
|-------|------|----------|
| **Phase 1** | M9-FS：S3 存储服务 + 文件 API | 2 天 |
| **Phase 2** | M9-SB：持久化沙箱实体 + 生命周期 + 命令执行 API | 3 天 |
| **Phase 3** | M9：Skill CRUD + 文件包管理 + Agent 绑定 + 渐进式注入 | 3 天 |
| **Phase 4** | M9-FE：Skill 管理页面 + Agent Skill 绑定 UI | 2 天 |
| **Phase 5** | M9-FE：沙箱管理页面 + Web Terminal + 文件浏览器 | 3 天 |

**总工期**：约 13 天（2-3 周）

---

## 7. 风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| MinIO S3 大文件同步耗时 | 沙箱启停延迟增加 | 增量同步（rsync/tar diff）；仅同步 `/workspace`，不同步系统目录 |
| 持久化沙箱 Pod 资源泄漏 | K8s 集群资源耗尽 | `AutoStop` 策略 + `SandboxPodPool` 清理机制扩展 |
| Skill 文件包过大撑爆上下文 | LLM 推理质量下降 | 渐进式披露机制 + 单文件读取限制 + 文件包总大小上限 |
| WebSocket Terminal 安全风险 | 命令注入攻击 | JWT 认证 + RBAC 权限校验 + 沙箱非特权容器隔离 |
