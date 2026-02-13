# CoreSRE — Agent Skills 与持久化沙箱 设计文档

**文档编号**: DESIGN-002  
**版本**: 1.0.0  
**状态**: DRAFT  
**创建日期**: 2026-02-13  
**关联文档**: [PRD-002](Agent-Skills-And-Persistent-Sandbox-PRD.md) | [OpenClaw 调研报告](OpenClaw-Skills-Research.md)  

---

## 1. 概览

本设计将三个紧密关联的子系统整合为一体：

```
┌──────────────────────────────────────────────────────────────────┐
│                    CoreSRE Skills & Sandbox 架构                  │
│                                                                   │
│  ┌─────────────┐   ┌─────────────────┐   ┌──────────────────┐   │
│  │ Agent Skills │   │ File Storage    │   │ Persistent       │   │
│  │ (M9)        │──▶│ (M9-FS)         │◀──│ Sandbox (M9-SB)  │   │
│  │             │   │                 │   │                  │   │
│  │ • CRUD      │   │ • MinIO/S3      │   │ • K8s Pod 管理   │   │
│  │ • 渐进注入   │   │ • Bucket 规范   │   │ • 生命周期状态机  │   │
│  │ • Agent绑定  │   │ • 文件浏览 API  │   │ • 工作区持久化    │   │
│  └──────┬──────┘   └────────┬────────┘   │ • Web Terminal   │   │
│         │                   │            └────────┬─────────┘   │
│         ▼                   ▼                     ▼              │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │              AgentResolverService (运行时)                 │   │
│  │  ToolRefs → AIFunction[]                                  │   │
│  │  SkillRefs → SkillPrompt + read_skill/read_skill_file     │   │
│  │  SandboxMode → Ephemeral Pod  or  Persistent SandboxInstance │ │
│  └──────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────┘
```

---

## 2. 子系统 A：文件系统（M9-FS）

### 2.1 架构

```
Frontend ──[presigned URL]──▶ MinIO
                                ▲
API ──[IFileStorageService]─────┘
```

### 2.2 接口设计

```csharp
// Application/Interfaces/IFileStorageService.cs
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

public record FileEntry(string Key, long Size, DateTimeOffset LastModified, string ContentType);
```

### 2.3 实现

```csharp
// Infrastructure/Services/Storage/MinioFileStorageService.cs
public class MinioFileStorageService : IFileStorageService
{
    private readonly IMinioClient _client;
    
    // MinIO 连接通过 Aspire Resource Reference 自动注入
    // AppHost: builder.AddMinioContainer("minio")
    // API:     builder.AddMinioClient("minio")
}
```

### 2.4 Bucket 规范

| Bucket | 用途 | 路径格式 | 生命周期 |
|--------|------|----------|----------|
| `coresre-skills` | Skill 文件包 | `{skillId}/scripts/`, `{skillId}/references/`, `{skillId}/assets/` | Skill 删除时清理 |
| `coresre-sandboxes` | 沙箱持久化工作区 | `{sandboxId}/workspace/` | 沙箱删除时清理 |
| `coresre-uploads` | 临时上传暂存 | `{uploadId}/filename` | 24h 自动过期 |

### 2.5 API 端点

```
POST   /api/files/{bucket}              ← 上传文件 (multipart/form-data)
GET    /api/files/{bucket}?prefix=xxx   ← 列出文件
GET    /api/files/{bucket}/{*key}       ← 下载文件 (或 redirect to presigned URL)
DELETE /api/files/{bucket}/{*key}       ← 删除文件
```

---

## 3. 子系统 B：Agent Skills（M9）

### 3.1 领域模型

```csharp
// Domain/Entities/SkillRegistration.cs
public class SkillRegistration : BaseEntity
{
    public string Name { get; private set; }           // 标识名（唯一）
    public string Description { get; private set; }    // LLM 触发依据（核心字段）
    public string Category { get; private set; }       // 分类标签
    public string Content { get; private set; }        // Markdown 指令正文
    public SkillScope Scope { get; private set; }      // Builtin/User/Project
    public SkillStatus Status { get; private set; }    // Active/Inactive
    public List<Guid> RequiresTools { get; private set; } = [];  // 依赖的工具 ID
    public bool HasFiles { get; private set; }         // 是否有 S3 文件包

    // Factory methods
    public static SkillRegistration Create(string name, string description, 
        string category, string content, SkillScope scope);
    public void Update(string name, string description, string category, string content);
    public void Activate();
    public void Deactivate();
    public void SetHasFiles(bool hasFiles);
    public void SetRequiresTools(List<Guid> toolIds);
}

// Domain/Enums/SkillScope.cs
public enum SkillScope { Builtin, User, Project }

// Domain/Enums/SkillStatus.cs
public enum SkillStatus { Active, Inactive }
```

### 3.2 数据库表

```sql
CREATE TABLE skill_registrations (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name             VARCHAR(128) NOT NULL UNIQUE,
    description      TEXT NOT NULL,
    category         VARCHAR(64) NOT NULL DEFAULT '',
    content          TEXT NOT NULL,                    -- Markdown 正文
    scope            VARCHAR(16) NOT NULL DEFAULT 'User',
    status           VARCHAR(16) NOT NULL DEFAULT 'Active',
    requires_tools   JSONB NOT NULL DEFAULT '[]',      -- Guid[]
    has_files        BOOLEAN NOT NULL DEFAULT false,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_skill_registrations_scope_status ON skill_registrations(scope, status);
```

### 3.3 LlmConfigVO 扩展

```csharp
// 在现有 LlmConfigVO 中新增：
public List<Guid>? SkillRefs { get; init; }  // 绑定的 Skill ID 列表
```

### 3.4 CQRS 命令与查询

| 操作 | 类型 | 端点 |
|------|------|------|
| `RegisterSkillCommand` | Command | `POST /api/skills` |
| `UpdateSkillCommand` | Command | `PUT /api/skills/{id}` |
| `DeleteSkillCommand` | Command | `DELETE /api/skills/{id}` |
| `GetSkillsQuery` | Query | `GET /api/skills` |
| `GetSkillByIdQuery` | Query | `GET /api/skills/{id}` |
| `UploadSkillFileCommand` | Command | `POST /api/skills/{id}/files` |
| `DeleteSkillFileCommand` | Command | `DELETE /api/skills/{id}/files/{*key}` |
| `ListSkillFilesQuery` | Query | `GET /api/skills/{id}/files` |

### 3.5 渐进式注入机制

```csharp
// Application/Skills/SkillPromptBuilder.cs
public class SkillPromptBuilder
{
    public string BuildSkillAwarePrompt(
        string baseSystemPrompt, 
        IReadOnlyList<SkillRegistration> skills)
    {
        var sb = new StringBuilder(baseSystemPrompt);
        sb.AppendLine("\n\n## Available Skills\n");
        sb.AppendLine("You have access to the following domain skills.");
        sb.AppendLine("Use the `read_skill` tool to load full instructions for a skill when needed.\n");
        
        foreach (var skill in skills)
        {
            sb.AppendLine($"- **{skill.Name}**: {skill.Description}");
        }
        
        return sb.ToString();
    }
}
```

### 3.6 Skill AIFunction 工具

```csharp
// Application/Tools/ReadSkillAIFunction.cs
public class ReadSkillAIFunction : AIFunction
{
    public override string Name => "read_skill";
    public override string Description => 
        "Load the full Markdown instructions for a skill by name. " +
        "Call this when you need detailed guidance for a specific skill.";
    
    // Parameters: { "skill_name": "string" }
    // Returns: Skill.Content (Markdown string)
}

// Application/Tools/ReadSkillFileAIFunction.cs
public class ReadSkillFileAIFunction : AIFunction
{
    public override string Name => "read_skill_file";
    public override string Description => 
        "Read a file from a skill's file package (scripts, references, assets).";
    
    // Parameters: { "skill_name": "string", "file_path": "string" }
    // Returns: File content (string) from S3
}
```

### 3.7 AgentResolverService 集成

```csharp
// 伪代码 — 在现有 ResolveAsync 流程中插入 Skill 逻辑

async Task<ResolvedAgent> ResolveAsync(AgentRegistration agent, ...)
{
    var tools = new List<AIFunction>();
    
    // 1. 现有: ToolRefs → AIFunction[]
    if (agent.LlmConfig?.ToolRefs?.Any() == true)
        tools.AddRange(await _toolFactory.CreateFunctionsAsync(agent.LlmConfig.ToolRefs));
    
    // 2. 新增: SkillRefs → SkillPrompt + AIFunction[]
    string systemPrompt = agent.LlmConfig?.Instructions ?? "";
    if (agent.LlmConfig?.SkillRefs?.Any() == true)
    {
        var skills = await _skillRepo.GetByIdsAsync(agent.LlmConfig.SkillRefs);
        var activeSkills = skills.Where(s => s.Status == SkillStatus.Active).ToList();
        
        // 2a. 将 Skill 摘要注入 SystemPrompt
        systemPrompt = _skillPromptBuilder.BuildSkillAwarePrompt(systemPrompt, activeSkills);
        
        // 2b. 注入 read_skill 工具
        tools.Add(new ReadSkillAIFunction(_skillRepo));
        
        // 2c. 如果有文件包，注入 read_skill_file 工具
        if (activeSkills.Any(s => s.HasFiles))
            tools.Add(new ReadSkillFileAIFunction(_fileStorage));
    }
    
    // 3. 现有: Sandbox → AIFunction[]
    if (agent.LlmConfig?.SandboxMode != SandboxMode.None)
        tools.AddRange(CreateSandboxTools(agent, ...));
    
    // 4. 构建 ChatClient
    return new ResolvedAgent(systemPrompt, tools);
}
```

---

## 4. 子系统 C：持久化沙箱（M9-SB）

### 4.1 领域模型

```csharp
// Domain/Entities/SandboxInstance.cs
public class SandboxInstance : BaseEntity
{
    public string Name { get; private set; }
    public SandboxStatus Status { get; private set; }       // Creating/Running/Stopped/Terminated
    public string SandboxType { get; private set; }         // SimpleBox ~ ComputerBox
    public string Image { get; private set; }
    public int CpuCores { get; private set; }
    public int MemoryMib { get; private set; }
    public string K8sNamespace { get; private set; }
    public int AutoStopMinutes { get; private set; }        // 0 = never
    public bool PersistWorkspace { get; private set; }
    
    public Guid? AgentId { get; private set; }              // 关联 Agent（可选）
    public DateTimeOffset? LastActivityAt { get; private set; }
    public string? PodName { get; private set; }            // 运行中的 K8s Pod 名称

    // Factory methods
    public static SandboxInstance Create(string name, string sandboxType, ...);
    public void MarkRunning(string podName);
    public void MarkStopped();
    public void MarkTerminated();
    public void RecordActivity();
    public void BindAgent(Guid agentId);
    public void UnbindAgent();
}

// Domain/Enums/SandboxStatus.cs
public enum SandboxStatus { Creating, Running, Stopped, Terminated }

// 扩展现有 SandboxMode 到 LlmConfigVO
public enum SandboxMode { None, Ephemeral, Persistent }
```

### 4.2 数据库表

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
    agent_id          UUID,                              -- FK to agent_registrations (nullable)
    last_activity_at  TIMESTAMPTZ,
    pod_name          VARCHAR(128),
    created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_sandbox_instances_status ON sandbox_instances(status);
CREATE INDEX idx_sandbox_instances_agent ON sandbox_instances(agent_id);
```

### 4.3 沙箱状态机

```
            Create()
               │
               ▼
         ┌─────────┐
         │ Creating │
         └────┬────┘
     Pod Ready │
               ▼
         ┌─────────┐  Stop()   ┌─────────┐
         │ Running  │────────▶│ Stopped  │
         └────┬────┘          └────┬────┘
              │                    │
              │ Delete()    Start()│ ──┐
              │                    │   │
              ▼                    ▼   │
         ┌────────────┐    ┌─────────┐ │
         │ Terminated │    │ Running │◀┘
         └────────────┘    └─────────┘
              ▲
              │ Delete()
              │
         ┌────┴────┐
         │ Stopped  │
         └─────────┘
```

### 4.4 持久化沙箱与临时沙箱的统一

```csharp
// 改造 KubernetesSandboxToolProvider
public class KubernetesSandboxToolProvider : ISandboxToolProvider
{
    public IReadOnlyList<AIFunction> CreateSandboxTools(
        Guid agentId, string conversationId, LlmConfigVO llmConfig)
    {
        var sandboxMode = llmConfig.SandboxMode ?? SandboxMode.None;
        
        ISandboxBox box = sandboxMode switch
        {
            SandboxMode.Ephemeral => 
                // 现有行为：创建临时 Pod，对话结束销毁
                _podPool.GetOrCreate($"{agentId:N}/{conversationId}", llmConfig),
            
            SandboxMode.Persistent =>
                // 新行为：获取/创建持久化沙箱的 Pod
                _persistentSandboxManager.GetOrStartAsync(
                    llmConfig.SandboxInstanceId ?? CreateNewPersistentSandbox(agentId, llmConfig)),
            
            _ => throw new InvalidOperationException("Sandbox not enabled")
        };
        
        // AIFunction 工具列表保持一致
        return CreateToolsForBox(box, llmConfig.SandboxType);
    }
}
```

### 4.5 工作区同步流程

```
沙箱启动 (Start):
┌──────────┐    ┌──────────┐    ┌──────────┐
│ Create   │──▶│ Copy S3  │──▶│ Running  │
│ K8s Pod  │   │→ /workspace│   │          │
└──────────┘   │→ /skills/  │   └──────────┘
               └──────────┘

沙箱停止 (Stop):
┌──────────┐    ┌──────────┐    ┌──────────┐
│ Sync     │──▶│ Delete   │──▶│ Stopped  │
│ /workspace│   │ K8s Pod  │   │          │
│ → S3     │   │          │   └──────────┘
└──────────┘   └──────────┘
```

同步实现（Pod exec 方式）：

```bash
# 启动时：从 S3 恢复（在 Pod 内执行）
mc cp --recursive s3/coresre-sandboxes/{sandboxId}/workspace/ /workspace/
mc cp --recursive s3/coresre-skills/{skillId}/ /skills/{skillName}/

# 停止前：持久化到 S3（在 Pod 内执行）
mc cp --recursive /workspace/ s3/coresre-sandboxes/{sandboxId}/workspace/
```

实际实现使用 `IFileStorageService` 替代 mc CLI，通过 exec 管道传输 tar 流。

### 4.6 自动停止后台服务

```csharp
// Infrastructure/Services/Sandbox/SandboxAutoStopService.cs
public class SandboxAutoStopService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // 每 5 分钟检查一次
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
            
            var runningSandboxes = await _repo.GetByStatusAsync(SandboxStatus.Running, ct);
            foreach (var sandbox in runningSandboxes)
            {
                if (sandbox.AutoStopMinutes > 0 &&
                    sandbox.LastActivityAt.HasValue &&
                    DateTimeOffset.UtcNow - sandbox.LastActivityAt.Value 
                        > TimeSpan.FromMinutes(sandbox.AutoStopMinutes))
                {
                    await _sandboxManager.StopAsync(sandbox.Id, ct);
                }
            }
        }
    }
}
```

### 4.7 CQRS 命令与查询

| 操作 | 类型 | 端点 |
|------|------|------|
| `CreateSandboxCommand` | Command | `POST /api/sandboxes` |
| `StartSandboxCommand` | Command | `POST /api/sandboxes/{id}/start` |
| `StopSandboxCommand` | Command | `POST /api/sandboxes/{id}/stop` |
| `DeleteSandboxCommand` | Command | `DELETE /api/sandboxes/{id}` |
| `UpdateSandboxCommand` | Command | `PUT /api/sandboxes/{id}` |
| `ExecInSandboxCommand` | Command | `POST /api/sandboxes/{id}/exec` |
| `GetSandboxesQuery` | Query | `GET /api/sandboxes` |
| `GetSandboxByIdQuery` | Query | `GET /api/sandboxes/{id}` |

### 4.8 WebSocket Terminal 端点

```csharp
// Endpoints/SandboxTerminalEndpoints.cs
app.MapGet("/api/sandboxes/{id}/terminal", async (
    Guid id, HttpContext ctx, ISandboxTerminalService terminalService) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
        return Results.BadRequest("WebSocket required");
    
    var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    await terminalService.HandleTerminalSession(id, ws, ctx.RequestAborted);
});
```

底层通过 K8s exec WebSocket API 实现双向流代理：

```
Browser ←[WebSocket]→ API ←[K8s Exec WebSocket]→ Pod (/bin/sh)
```

---

## 5. 前端设计

### 5.1 页面路由

| 路由 | 页面 | 组件 |
|------|------|------|
| `/skills` | Skill 列表 | `SkillListPage` |
| `/skills/:id` | Skill 详情/编辑 | `SkillDetailPage` |
| `/sandboxes` | 沙箱列表 | `SandboxListPage` |
| `/sandboxes/:id` | 沙箱详情 | `SandboxDetailPage` |

### 5.2 Skill 编辑器

复用现有 `WorkflowCodeEditor` 组件，配置 `language="markdown"`：

```tsx
<WorkflowCodeEditor
  value={skill.content}
  onChange={setContent}
  language="markdown"    // Monaco Markdown 模式
  height={400}
/>
```

### 5.3 Web Terminal 组件

```tsx
// components/sandboxes/WebTerminal.tsx
import { Terminal } from 'xterm';
import { FitAddon } from 'xterm-addon-fit';

function WebTerminal({ sandboxId }: { sandboxId: string }) {
    const termRef = useRef<HTMLDivElement>(null);
    
    useEffect(() => {
        const term = new Terminal({ cursorBlink: true, theme: { background: '#1e1e1e' } });
        const fitAddon = new FitAddon();
        term.loadAddon(fitAddon);
        term.open(termRef.current!);
        fitAddon.fit();
        
        const ws = new WebSocket(`ws://.../api/sandboxes/${sandboxId}/terminal`);
        ws.onmessage = (e) => term.write(e.data);
        term.onData((data) => ws.send(data));
        
        return () => { ws.close(); term.dispose(); };
    }, [sandboxId]);
    
    return <div ref={termRef} className="h-[400px]" />;
}
```

### 5.4 文件浏览器组件

使用 shadcn/ui 的 Tree 组件展示沙箱文件树，支持：
- 展开/折叠目录
- 点击文件预览（文本文件）
- 右键菜单：下载/删除
- 拖拽上传

---

## 6. DI 注册

```csharp
// Infrastructure/DependencyInjection.cs — 新增注册

// File Storage
services.AddScoped<IFileStorageService, MinioFileStorageService>();

// Skills
services.AddScoped<ISkillRegistrationRepository, SkillRegistrationRepository>();
services.AddScoped<SkillPromptBuilder>();

// Persistent Sandbox
services.AddScoped<ISandboxInstanceRepository, SandboxInstanceRepository>();
services.AddScoped<IPersistentSandboxManager, PersistentSandboxManager>();
services.AddScoped<ISandboxTerminalService, KubernetesSandboxTerminalService>();
services.AddHostedService<SandboxAutoStopService>();
```

---

## 7. 数据流全景

```
用户上传 Skill
     │
     ▼
┌─────────────────┐     ┌──────────────┐
│ POST /api/skills │────▶│ PostgreSQL   │  Skill 元数据
│                  │     │ skill_regs   │
│ POST /api/skills │────▶│              │
│  /{id}/files     │     └──────────────┘
│                  │
│ [multipart file] │────▶┌──────────────┐
│                  │     │ MinIO S3     │  Skill 文件包
└─────────────────┘     │ skills/{id}/ │
                         └──────┬───────┘
                                │
               对话开始时        │ 拷贝文件到沙箱
                                ▼
                         ┌──────────────┐
Agent Chat               │ K8s Pod      │  /skills/{name}/scripts/
  │                      │ (沙箱)        │  /workspace/
  │ SkillRefs            │              │
  ▼                      └──────┬───────┘
┌─────────────────┐             │
│ SystemPrompt    │             │ 对话中 LLM 调用
│ + Skill 摘要列表 │             │ run_command("/skills/...")
│                  │             │
│ + read_skill()  │◀── LLM ────┘
│ + read_skill_file()│
│ + run_command() │
└─────────────────┘
                                │
               沙箱停止时        │ 持久化工作区
                                ▼
                         ┌──────────────┐
                         │ MinIO S3     │
                         │ sandboxes/   │
                         │ {id}/workspace/│
                         └──────────────┘
```

---

## 8. 安全设计

| 安全点 | 措施 |
|--------|------|
| S3 访问 | API 内部访问，前端通过预签名 URL（5 分钟过期）；不暴露 MinIO 凭据 |
| 沙箱隔离 | 非特权容器 (`Privileged=false, AllowPrivilegeEscalation=false`)；K8s NetworkPolicy 限制出入流量 |
| WebSocket Terminal | JWT 认证中间件；RBAC 校验用户对沙箱的访问权限 |
| Skill 文件 | 上传文件类型校验（禁止可执行二进制，仅允许文本/资产文件） |
| 文件大小 | 单文件限制 10MB；Skill 文件包总计限制 100MB |
| 沙箱资源 | CPU/Memory limit 硬限（LimitRange）；Pod 数量上限（ResourceQuota） |

---

## 9. 可观测性

| 事件 | OTel 属性 |
|------|-----------|
| Skill 加载 | `skill.name`, `skill.id`, `agent.id` |
| S3 文件操作 | `s3.bucket`, `s3.key`, `s3.operation` |
| 沙箱生命周期 | `sandbox.id`, `sandbox.status`, `sandbox.type`, `sandbox.pod_name` |
| 沙箱命令执行 | `sandbox.id`, `exec.command`, `exec.exit_code`, `exec.duration_ms` |
| 工作区同步 | `sandbox.id`, `sync.direction` (upload/download), `sync.bytes` |

---

## 10. 迁移策略

### 10.1 现有临时沙箱兼容

- `LlmConfigVO.EnableSandbox = true` → 映射为 `SandboxMode = Ephemeral`
- 临时沙箱行为完全不变（`SandboxPodPool` 继续管理）
- 数据库 migration 需将 `enable_sandbox: true` 转换为 `sandbox_mode: "Ephemeral"`

### 10.2 渐进式部署

1. **Phase 1**: 部署文件系统服务，验证 MinIO 集成
2. **Phase 2**: 部署持久化沙箱（与临时沙箱并行运行）
3. **Phase 3**: 部署 Skills 系统
4. **Phase 4**: 前端页面上线
