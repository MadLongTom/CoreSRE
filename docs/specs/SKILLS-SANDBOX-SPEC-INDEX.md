# CoreSRE — Agent Skills 与持久化沙箱 Spec 总览

**文档编号**: SKILLS-SANDBOX-SPEC-INDEX  
**版本**: 1.0.0  
**创建日期**: 2026-02-13  
**关联文档**: [PRD-002](../Agent-Skills-And-Persistent-Sandbox-PRD.md) | [设计文档](../Agent-Skills-And-Persistent-Sandbox-Design.md) | [OpenClaw 调研报告](../OpenClaw-Skills-Research.md) | [SPEC-INDEX](SPEC-INDEX.md)

> 本文档将 Agent Skills 与持久化沙箱设计拆分为可独立交付的 Spec 清单。  
> 目标：为 CoreSRE Agent 提供模块化知识注入（Skills）和有状态执行环境（持久化沙箱）。  
> 原则：渐进式交付，每个 Phase 可独立验收；保持 C# / .NET 技术栈和现有 Clean Architecture 分层。  
> 前置依赖：AppHost MinIO 配置（已完成）、K8s 沙箱基础设施（已完成）。

---

## 模块依赖矩阵

```
SPEC-090 (S3 文件系统)           ← 无依赖，最先实现
    │
    ├──▶ SPEC-091 (持久化沙箱)    ← 依赖 SPEC-090（工作区 S3 持久化）
    │       │
    │       ├──▶ SPEC-093 (沙箱前端 + Web Terminal)  ← 依赖 SPEC-091 API
    │       │
    │       └──▶ SPEC-092 (Agent Skills) ← 依赖 SPEC-090（文件包）+ SPEC-091（Skill 文件挂载）
    │               │
    │               └──▶ SPEC-094 (Skills 前端)  ← 依赖 SPEC-092 API
    │
    └──▶ 可独立使用（供后续功能复用的基础服务）
```

---

## Phase 1：S3 文件系统基础服务

### SPEC-090: MinIO 文件存储服务

**优先级**: P1（Phase 1 — 基础设施，所有后续 Spec 的前置依赖）  
**预估工期**: 2 天  
**前置依赖**: AppHost MinIO 配置（已完成）

**简述**: 实现基于 MinIO (S3 兼容) 的统一文件存储服务，为 Skills 文件包、沙箱工作区持久化、临时上传等场景提供底层存储能力。通过 `IFileStorageService` 接口抽象 S3 操作，所有上层模块通过此接口访问对象存储，不直接依赖 MinIO SDK。

**核心工作**:

| 任务 | 说明 | 涉及层 |
|------|------|--------|
| FS-1 | 定义 `IFileStorageService` 接口（Upload/Download/Delete/List/Exists/GetPresignedUrl/DeletePrefix） | Application |
| FS-2 | 实现 `MinioFileStorageService`，通过 Aspire Resource Reference 注入 MinIO 连接 | Infrastructure |
| FS-3 | Bucket 初始化 — 应用启动时确保 `coresre-skills`、`coresre-sandboxes`、`coresre-uploads` 三个 Bucket 存在 | Infrastructure |
| FS-4 | 文件浏览 REST API 端点 — 上传(multipart)、列出(prefix)、下载(presigned redirect)、删除 | Endpoints |
| FS-5 | AppHost 集成 — 确保 MinIO Aspire client NuGet 包配置正确，连接字符串自动注入 | AppHost |
| FS-6 | 单元测试 — 使用 Testcontainers MinIO 或 Mock 进行 `IFileStorageService` 各方法测试 | Tests |

**领域模型**:
- `FileEntry` (Record): `Key`, `Size`, `LastModified`, `ContentType`
- 无数据库表（纯 S3 对象存储）

**接口定义**:

```csharp
public interface IFileStorageService
{
    Task<string> UploadAsync(string bucket, string key, Stream content, 
                             string contentType, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string bucket, string key, CancellationToken ct = default);
    Task DeleteAsync(string bucket, string key, CancellationToken ct = default);
    Task<IReadOnlyList<FileEntry>> ListAsync(string bucket, string prefix, 
                                              CancellationToken ct = default);
    Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default);
    Task<string> GetPresignedUrlAsync(string bucket, string key, 
                                      TimeSpan expiry, CancellationToken ct = default);
    Task DeletePrefixAsync(string bucket, string prefix, CancellationToken ct = default);
}
```

**端点**:

```
POST   /api/files/{bucket}              ← 上传文件 (multipart/form-data)
GET    /api/files/{bucket}?prefix=xxx   ← 列出文件
GET    /api/files/{bucket}/{*key}       ← 下载 (redirect to presigned URL)
DELETE /api/files/{bucket}/{*key}       ← 删除文件
```

**Bucket 规范**:

| Bucket | 用途 | 路径格式 |
|--------|------|----------|
| `coresre-skills` | Skill 文件包 | `{skillId}/scripts/`, `{skillId}/references/`, `{skillId}/assets/` |
| `coresre-sandboxes` | 沙箱持久化工作区 | `{sandboxId}/workspace/` |
| `coresre-uploads` | 临时上传暂存 | `{uploadId}/filename` |

**DI 注册**:
```csharp
services.AddScoped<IFileStorageService, MinioFileStorageService>();
```

**验收标准**:
1. **Given** MinIO 通过 Aspire AppHost 启动，**When** 上传一个文件到 `coresre-skills/test/hello.txt`，**Then** 通过 `ListAsync("coresre-skills", "test/")` 可看到该文件，通过 `DownloadAsync` 可读取到正确内容。
2. **Given** 存在一个已上传文件，**When** 调用 `GetPresignedUrlAsync`，**Then** 返回的 URL 可在 5 分钟内直接 HTTP GET 下载该文件。
3. **Given** 调用 `DeletePrefixAsync("coresre-skills", "test/")`，**Then** 该前缀下所有文件被删除。
4. **Given** 通过 `POST /api/files/coresre-uploads` 上传一个 multipart 文件，**Then** 返回 201 和文件 key，后续可通过 GET 端点下载。

---

## Phase 2：持久化沙箱

### SPEC-091: 持久化沙箱管理

**优先级**: P1（Phase 2 — 核心基础设施）  
**预估工期**: 3 天  
**前置依赖**: SPEC-090（文件存储服务）、现有 K8s 沙箱（`SandboxPodPool`, `KubernetesSandboxBox`）

**简述**: 将现有临时沙箱（每对话创建/销毁 Pod）升级为支持持久化沙箱。持久化沙箱是独立于对话的有状态 K8s Pod，具有完整的生命周期管理（创建/启动/停止/删除），工作区目录 (`/workspace`) 在停止时自动持久化到 S3、启动时自动恢复。用户可通过 API 管理沙箱并直接执行命令，沙箱可被多次对话复用。现有临时沙箱行为保持不变（向后兼容）。

**核心工作**:

| 任务 | 说明 | 涉及层 |
|------|------|--------|
| SB-1 | `SandboxInstance` 领域实体 — 状态机: Creating → Running → Stopped → Terminated | Domain |
| SB-2 | `SandboxStatus` / `SandboxMode` 枚举 — Status(Creating/Running/Stopped/Terminated), Mode(None/Ephemeral/Persistent) | Domain |
| SB-3 | EF Migration — `sandbox_instances` 表 | Infrastructure |
| SB-4 | `ISandboxInstanceRepository` — CRUD + 按状态/Agent查询 | Application / Infrastructure |
| SB-5 | `IPersistentSandboxManager` 接口与实现 — 管理持久化 Pod 的创建/启动(含 S3 恢复)/停止(含 S3 持久化)/删除 | Application / Infrastructure |
| SB-6 | 工作区同步 — 启动时从 S3 恢复 `/workspace`；停止时将 `/workspace` 同步到 S3 | Infrastructure |
| SB-7 | `LlmConfigVO` 扩展 — 新增 `SandboxMode`(替代 `EnableSandbox`) 和 `SandboxInstanceId` 字段 | Domain |
| SB-8 | `KubernetesSandboxToolProvider` 改造 — 支持 Persistent 模式时获取已存在的 Pod | Infrastructure |
| SB-9 | `SandboxAutoStopService` — 后台服务定期检查，超过 AutoStopMinutes 无活动的沙箱自动停止 | Infrastructure |
| SB-10 | CQRS Commands/Queries — Create/Start/Stop/Delete/Update/Exec/Get/List | Application |
| SB-11 | REST API 端点 — 沙箱 CRUD + 生命周期操作 + 命令执行 | Endpoints |
| SB-12 | WebSocket Terminal — `/api/sandboxes/{id}/terminal` 双向流代理到 K8s exec WebSocket | Endpoints / Infrastructure |
| SB-13 | 数据迁移 — 现有 `EnableSandbox=true` 转换为 `SandboxMode=Ephemeral` | Infrastructure |
| SB-14 | 单元测试 + 集成测试 — 沙箱状态机、工作区同步、自动停止 | Tests |

**领域模型**:

```csharp
public class SandboxInstance : BaseEntity
{
    public string Name { get; private set; }
    public SandboxStatus Status { get; private set; }
    public string SandboxType { get; private set; }
    public string Image { get; private set; }
    public int CpuCores { get; private set; }
    public int MemoryMib { get; private set; }
    public string K8sNamespace { get; private set; }
    public int AutoStopMinutes { get; private set; }
    public bool PersistWorkspace { get; private set; }
    public Guid? AgentId { get; private set; }
    public DateTimeOffset? LastActivityAt { get; private set; }
    public string? PodName { get; private set; }
}
```

**数据库表**:

```sql
CREATE TABLE sandbox_instances (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name              VARCHAR(128) NOT NULL,
    status            VARCHAR(16) NOT NULL DEFAULT 'Creating',
    sandbox_type      VARCHAR(32) NOT NULL DEFAULT 'SimpleBox',
    image             VARCHAR(256) NOT NULL,
    cpu_cores         INT NOT NULL DEFAULT 1,
    memory_mib        INT NOT NULL DEFAULT 512,
    k8s_namespace     VARCHAR(64) NOT NULL DEFAULT 'coresre-sandbox',
    auto_stop_minutes INT NOT NULL DEFAULT 30,
    persist_workspace BOOLEAN NOT NULL DEFAULT true,
    agent_id          UUID REFERENCES agent_registrations(id) ON DELETE SET NULL,
    last_activity_at  TIMESTAMPTZ,
    pod_name          VARCHAR(128),
    created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

**端点**:

```
POST   /api/sandboxes                  ← 创建沙箱
GET    /api/sandboxes                  ← 列出沙箱 (?status=&agentId=)
GET    /api/sandboxes/{id}             ← 沙箱详情
PUT    /api/sandboxes/{id}             ← 更新配置 (仅 Stopped 状态)
DELETE /api/sandboxes/{id}             ← 终止并删除
POST   /api/sandboxes/{id}/start       ← 启动已停止的沙箱
POST   /api/sandboxes/{id}/stop        ← 停止运行中的沙箱
POST   /api/sandboxes/{id}/exec        ← 执行命令 (JSON: {command, args[]})
GET    /api/sandboxes/{id}/terminal    ← WebSocket Terminal 升级
```

**状态机**:

```
Creating ──[Pod Ready]──▶ Running ──[Stop]──▶ Stopped ──[Start]──▶ Running
                              │                   │
                              └──[Delete]──▶ Terminated ◀──[Delete]──┘
```

**验收标准**:
1. **Given** 用户通过 `POST /api/sandboxes` 创建一个 SimpleBox 沙箱，**When** Pod 启动完成，**Then** 状态变为 Running，`GET /api/sandboxes/{id}` 返回 Running 状态和 PodName。
2. **Given** 一个 Running 沙箱中用户通过 `exec` 创建了文件 `/workspace/test.txt`，**When** 调用 `POST /api/sandboxes/{id}/stop`，**Then** 沙箱状态变为 Stopped，`/workspace/test.txt` 被同步到 S3 `coresre-sandboxes/{id}/workspace/test.txt`。
3. **Given** 一个 Stopped 沙箱，**When** 调用 `POST /api/sandboxes/{id}/start`，**Then** 新 Pod 启动后自动从 S3 恢复 `/workspace/test.txt`，通过 `exec` 可读取该文件。
4. **Given** 一个沙箱配置了 `AutoStopMinutes=30` 且 `LastActivityAt` 超过 30 分钟，**When** `SandboxAutoStopService` 执行检查，**Then** 沙箱自动停止。
5. **Given** 一个 Running 沙箱，**When** 通过 WebSocket 连接 `/api/sandboxes/{id}/terminal`，**Then** 可双向交互执行命令（输入 `ls` 返回目录列表）。
6. **Given** Agent 的 `LlmConfig.SandboxMode = Persistent` 且 `SandboxInstanceId` 指向一个 Stopped 沙箱，**When** 开始对话，**Then** 沙箱自动启动并恢复工作区，Agent 的沙箱工具操作在该持久化 Pod 内执行。
7. **Given** Agent 的 `LlmConfig.SandboxMode = Ephemeral`（或旧配置 `EnableSandbox = true`），**When** 开始对话，**Then** 行为与现有临时沙箱完全一致（向后兼容）。

---

## Phase 3：Agent Skills

### SPEC-092: Agent Skills 管理与渐进式注入

**优先级**: P1（Phase 3 — 核心功能）  
**预估工期**: 3 天  
**前置依赖**: SPEC-090（文件存储）、SPEC-091（持久化沙箱，Skill 文件挂载到沙箱）

**简述**: 实现 Agent Skills 系统——模块化的 Markdown 知识文档，可绑定到 Agent 以扩展其领域能力。Skill 不注册新工具，而是通过 SystemPrompt 注入"操作手册"，教 LLM 如何使用已有工具完成特定任务。采用借鉴自 OpenClaw 的渐进式披露机制：SystemPrompt 仅列出 Skill 名称 + 描述摘要（约 100 tokens/skill），LLM 通过 `read_skill` 工具按需加载完整 Markdown 正文。每个 Skill 可选关联 S3 文件包（scripts/references/assets），在沙箱启动时自动挂载到 `/skills/{skillName}/` 目录。

**核心工作**:

| 任务 | 说明 | 涉及层 |
|------|------|--------|
| SK-1 | `SkillRegistration` 领域实体 — Name, Description, Content(Markdown), Category, Scope, Status, RequiresTools, HasFiles | Domain |
| SK-2 | `SkillScope` / `SkillStatus` 枚举 | Domain |
| SK-3 | EF Migration — `skill_registrations` 表 | Infrastructure |
| SK-4 | `ISkillRegistrationRepository` — CRUD + 按 Scope/Status/Category 查询 | Application / Infrastructure |
| SK-5 | `LlmConfigVO` 扩展 — 新增 `SkillRefs: List<Guid>` 字段 | Domain |
| SK-6 | CQRS Commands — Register/Update/Delete Skill，Upload/Delete Skill Files | Application |
| SK-7 | CQRS Queries — GetSkills(分页/过滤)/GetSkillById/ListSkillFiles | Application |
| SK-8 | Skill REST API 端点 | Endpoints |
| SK-9 | `SkillPromptBuilder` — 将 Skill 摘要列表拼接到 SystemPrompt 尾部 | Application |
| SK-10 | `ReadSkillAIFunction` — LLM 工具，按 name 加载 Skill 完整 Content | Application |
| SK-11 | `ReadSkillFileAIFunction` — LLM 工具，从 S3 读取 Skill 文件包中的文件内容 | Application |
| SK-12 | `AgentResolverService` 集成 — 在 Agent 运行时注入 Skill Prompt + AIFunction 工具 | Application |
| SK-13 | Skill 文件挂载 — 沙箱启动时将 Skill 绑定的 S3 文件拷贝到 Pod 的 `/skills/{name}/` | Infrastructure |
| SK-14 | 工具依赖门控 — 绑定 Skill 时验证 Agent 的 ToolRefs 覆盖 RequiresTools，不满足警告 | Application |
| SK-15 | 种子数据 — 预置 Builtin Skills（incident-response, code-review, database-ops 等） | Infrastructure |
| SK-16 | 单元测试 — SkillPromptBuilder、ReadSkillAIFunction、集成测试 | Tests |

**领域模型**:

```csharp
public class SkillRegistration : BaseEntity
{
    public string Name { get; private set; }
    public string Description { get; private set; }
    public string Category { get; private set; }
    public string Content { get; private set; }
    public SkillScope Scope { get; private set; }
    public SkillStatus Status { get; private set; }
    public List<Guid> RequiresTools { get; private set; } = [];
    public bool HasFiles { get; private set; }
}
```

**数据库表**:

```sql
CREATE TABLE skill_registrations (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name             VARCHAR(128) NOT NULL UNIQUE,
    description      TEXT NOT NULL,
    category         VARCHAR(64) NOT NULL DEFAULT '',
    content          TEXT NOT NULL,
    scope            VARCHAR(16) NOT NULL DEFAULT 'User',
    status           VARCHAR(16) NOT NULL DEFAULT 'Active',
    requires_tools   JSONB NOT NULL DEFAULT '[]',
    has_files        BOOLEAN NOT NULL DEFAULT false,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

**端点**:

```
POST   /api/skills                     ← 创建 Skill
GET    /api/skills                     ← 列出 Skills (?scope=&status=&category=&search=)
GET    /api/skills/{id}                ← Skill 详情
PUT    /api/skills/{id}                ← 更新 Skill
DELETE /api/skills/{id}                ← 删除 Skill (同时清理 S3 文件包)
POST   /api/skills/{id}/files          ← 上传文件到 Skill 文件包 (multipart)
GET    /api/skills/{id}/files          ← 列出 Skill 文件包
DELETE /api/skills/{id}/files/{*key}   ← 删除 Skill 文件包中的文件
```

**渐进式注入流程**:

```
Agent 对话开始
    │
    ▼
加载 Agent.LlmConfig.SkillRefs
    │
    ▼
从 DB 查询 SkillRegistration[] (仅 Active 状态)
    │
    ├──▶ SkillPromptBuilder 拼接摘要到 SystemPrompt:
    │    "## Available Skills
    │     Use `read_skill` to load full instructions.
    │     - **incident-response**: SRE 事件响应流程..."
    │
    ├──▶ 注入 ReadSkillAIFunction 到 Tools
    │
    └──▶ 如果任一 Skill 有文件包, 注入 ReadSkillFileAIFunction 到 Tools
```

**Skill 文件挂载时序**:

```
沙箱启动 (SPEC-091 StopAsync 流程中插入)
    │
    ▼
读取 Agent 绑定的 SkillRefs
    │
    ▼
对于每个 HasFiles=true 的 Skill:
    IFileStorageService.ListAsync("coresre-skills", "{skillId}/")
    │
    ▼
    通过 Pod exec 流式传输:
    tar -C /skills/{skillName}/ -xf - < S3 Stream
```

**验收标准**:
1. **Given** 用户创建了一个 Skill（name=`github-ops`, description=`GitHub 操作指南`, content=`# GitHub...`），**When** 将该 Skill 绑定到 Agent A 的 SkillRefs，**Then** Agent A 的 SystemPrompt 末尾出现 `**github-ops**: GitHub 操作指南`。
2. **Given** Agent A 绑定了 Skill `github-ops`，**When** LLM 调用 `read_skill({ skill_name: "github-ops" })`，**Then** 返回该 Skill 的完整 Markdown Content。
3. **Given** Skill `database-ops` 有文件包 `scripts/backup.sh`，Agent B 使用持久化沙箱且绑定了该 Skill，**When** 沙箱启动，**Then** Pod 内 `/skills/database-ops/scripts/backup.sh` 存在且可执行。
4. **Given** LLM 调用 `read_skill_file({ skill_name: "database-ops", file_path: "scripts/backup.sh" })`，**Then** 返回 `backup.sh` 的文件内容。
5. **Given** Skill `monitoring-analysis` 声明 `RequiresTools = [prometheus-tool-id]`，Agent C 的 ToolRefs 不包含该工具，**When** 绑定该 Skill 到 Agent C，**Then** API 返回成功但附带警告信息。
6. **Given** 系统初始化，**Then** `skill_registrations` 表中有 5 条 `Scope=Builtin` 的种子 Skill 记录。

---

## Phase 4：前端 — Skill 管理

### SPEC-093: 沙箱管理前端与 Web Terminal

**优先级**: P1（Phase 4 — 前端）  
**预估工期**: 3 天  
**前置依赖**: SPEC-091（沙箱管理 API + WebSocket Terminal 端点）

**简述**: 实现沙箱管理的完整前端页面，包括沙箱列表/详情页、生命周期操作（启动/停止/删除）、基于 xterm.js 的 Web Terminal（通过 WebSocket 连接沙箱 Shell）、以及沙箱内文件浏览器（读取 `/workspace` 目录）。

**核心工作**:

| 任务 | 说明 | 涉及文件 |
|------|------|----------|
| SBFE-1 | 沙箱列表页 — 表格展示(名称/状态灯/类型/关联Agent/资源/创建时间)，按状态过滤，创建沙箱对话框 | `SandboxListPage.tsx` |
| SBFE-2 | 沙箱详情页 — 状态卡片(Running/Stopped + 运行时间/CPU/Memory)、配置信息、操作按钮(Start/Stop/Restart/Delete) | `SandboxDetailPage.tsx` |
| SBFE-3 | Web Terminal 组件 — `xterm.js` + `xterm-addon-fit` + WebSocket 双向流、终端自适应 resize | `WebTerminal.tsx` |
| SBFE-4 | 文件浏览器组件 — 树状展示 `/workspace` 文件、文本预览、上传/下载 | `SandboxFileBrowser.tsx` |
| SBFE-5 | API 客户端 — Sandbox CRUD + lifecycle + exec hooks | `hooks/useSandboxes.ts` |
| SBFE-6 | 路由注册 — `/sandboxes`、`/sandboxes/:id` | `App.tsx` |
| SBFE-7 | 沙箱 Tab 集成 — 在 Agent 详情页添加"沙箱"Tab，展示关联的持久化沙箱 | `AgentDetailPage` 修改 |

**第三方依赖**:
- `xterm` + `@xterm/addon-fit` — Terminal 渲染
- `react-arborist` 或 shadcn Tree — 文件树

**页面路由**: `/sandboxes`、`/sandboxes/:id`

**验收标准**:
1. **Given** 用户访问 `/sandboxes`，**Then** 看到沙箱列表表格，Running 沙箱显示绿色指示灯、Stopped 显示灰色。
2. **Given** 用户点击"创建沙箱"，选择 CodeBox 类型并填写名称，**Then** 创建成功后在列表中新增一行，状态从 Creating 变为 Running。
3. **Given** 用户在沙箱详情页点击 Terminal Tab，**Then** 打开 xterm.js 终端，可交互执行 `ls`、`echo hello` 等命令。
4. **Given** 用户在 Terminal 中执行 `echo "test" > /workspace/test.txt`，切换到 Files Tab，**Then** 文件浏览器中显示 `test.txt`，点击可预览内容 `test`。
5. **Given** 用户点击"停止"按钮，**Then** 状态变为 Stopped，Terminal 断开连接。

---

### SPEC-094: Skills 管理前端与 Agent 绑定 UI

**优先级**: P1（Phase 4 — 前端）  
**预估工期**: 2 天  
**前置依赖**: SPEC-092（Skill API）

**简述**: 实现 Skill 管理的完整前端页面，包括 Skill 列表/创建/编辑/详情页面（含 Monaco Markdown 编辑器和文件包管理）。同时在 Agent 编辑页面新增 Skill 绑定 UI，支持多选绑定/解绑 Skill，展示依赖门控警告。

**核心工作**:

| 任务 | 说明 | 涉及文件 |
|------|------|----------|
| SKFE-1 | Skill 列表页 — 表格(名称/描述/分类Badge/Scope Badge/状态)，按分类/状态/Scope过滤，搜索 | `SkillListPage.tsx` |
| SKFE-2 | Skill 创建/编辑页 — 基本信息表单(名称/描述/分类/Scope) + Monaco Markdown 编辑器(Content) | `SkillEditorPage.tsx` |
| SKFE-3 | Skill 文件包管理区 — 文件上传(拖拽/按钮)、文件树展示、删除操作 | `SkillFileManager.tsx` |
| SKFE-4 | Skill 详情页 — Markdown 渲染预览 + 文件列表 + 元数据 | `SkillDetailPage.tsx` |
| SKFE-5 | Agent Skill 绑定 UI — Agent 编辑页新增"Skills"区域，多选列表(名称+描述+分类Tag)，绑定/解绑 | `AgentSkillBinder.tsx` |
| SKFE-6 | 依赖门控警告 — 绑定 Skill 时检查 RequiresTools vs Agent ToolRefs，不满足时显示 Warning | `AgentSkillBinder.tsx` |
| SKFE-7 | API 客户端 — Skills CRUD + file management hooks | `hooks/useSkills.ts` |
| SKFE-8 | 路由注册 — `/skills`、`/skills/new`、`/skills/:id` | `App.tsx` |

**页面路由**: `/skills`、`/skills/new`、`/skills/:id`

**验收标准**:
1. **Given** 用户访问 `/skills`，**Then** 看到 Skill 列表表格，Builtin 类型显示"内建"Badge。
2. **Given** 用户创建 Skill，在 Monaco 编辑器中输入 Markdown 内容，**Then** 保存成功后在列表中可看到新 Skill。
3. **Given** 用户在 Skill 编辑页上传 `scripts/deploy.sh`，**Then** 文件包管理区展示文件树，显示 `scripts/deploy.sh`。
4. **Given** 用户在 Agent 编辑页的 Skills 区域选择两个 Skill 并保存，**Then** Agent 的 `SkillRefs` 更新为对应的两个 Guid。
5. **Given** Skill A 声明 `RequiresTools = [tool-1]`，Agent 未绑定 tool-1，**When** 用户尝试绑定 Skill A，**Then** UI 显示黄色警告"该 Skill 需要工具 xxx，当前 Agent 未绑定"。

---

## Spec 执行优先级与依赖总览

```
Phase 1 (基础设施 — 2天)
  └── SPEC-090: MinIO 文件存储服务

Phase 2 (核心后端 — 3天)
  └── SPEC-091: 持久化沙箱管理          ← 依赖 SPEC-090

Phase 3 (核心后端 — 3天)
  └── SPEC-092: Agent Skills 管理与注入  ← 依赖 SPEC-090 + SPEC-091

Phase 4 (前端 — 5天, 可并行)
  ├── SPEC-093: 沙箱管理前端 + WebTerminal  ← 依赖 SPEC-091
  └── SPEC-094: Skills 管理前端 + Agent绑定  ← 依赖 SPEC-092
```

**总工期**: 约 13 天（考虑 Phase 4 内并行可压缩至 11 天）

---

## 与主 SPEC-INDEX 的关系

本 SPEC-INDEX 编号范围 `090-094`，归属新模块 **M9: Agent Skills & Persistent Sandbox**。应在主 [SPEC-INDEX](SPEC-INDEX.md) 中新增以下条目：

```markdown
P1-Skills (Agent Skills & 持久化沙箱 — MVP 可用)
  │   详见 [SKILLS-SANDBOX-SPEC-INDEX](SKILLS-SANDBOX-SPEC-INDEX.md)
  ├── SPEC-090: MinIO 文件存储服务
  ├── SPEC-091: 持久化沙箱管理
  ├── SPEC-092: Agent Skills 管理与渐进式注入 ★
  ├── SPEC-093: 沙箱管理前端与 Web Terminal
  └── SPEC-094: Skills 管理前端与 Agent 绑定 UI
```

*每个 SPEC 展开为详细实现时，遵循 Constitution 五步流程：Spec → Test → Interface → Implement → Verify。*
