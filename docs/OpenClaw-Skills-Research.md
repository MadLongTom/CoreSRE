# OpenClaw Agent Skills 调研报告

> 调研日期: 2026-02-13  
> 调研对象: [OpenClaw](https://github.com/openclaw/openclaw) (`.reference/codes/openclaw`)

## 一、OpenClaw 项目概览

OpenClaw 是一个基于 Node.js/TypeScript 的 AI Agent 框架，其核心理念是 **Skills = Markdown 指令，而非代码**。它将 Agent 能力拆分为两个正交层：

| 层级 | 名称 | 角色 |
|------|------|------|
| **执行层** | Tools (工具) | 硬编码的可执行函数，提供实际能力（文件操作、代码执行、MCP 调用等） |
| **知识层** | Skills (技能) | Markdown 文档，教 LLM *如何使用*已有工具来完成特定领域任务 |

**核心洞察**：Skills 不注册新的 Tool，而是通过自然语言向 LLM 注入"操作手册"，让 LLM 学会使用已有 Tools 完成复杂工作流。

---

## 二、Tools（工具执行层）

### 2.1 工具接口

```typescript
interface AnyAgentTool {
  name: string;
  description: string;
  parameters: TSchema;            // TypeBox JSON Schema
  execute: (
    toolCallId: string, 
    params: Record<string, unknown>,
    signal: AbortSignal, 
    onUpdate: UpdateCallback
  ) => Promise<AgentToolResult>;
}
```

### 2.2 内建工具分类

| 分类 | 工具名 | 说明 |
|------|--------|------|
| **编码** | `read`, `write`, `edit`, `multi_edit`, `exec`, `apply_patch` | 文件CRUD + Shell执行 |
| **浏览器** | `browser` | Playwright 驱动的浏览器控制 |
| **Web** | `web_search`, `web_fetch` | 搜索引擎 + URL 抓取 |
| **记忆** | `memory_search` | 向量存储语义搜索 |
| **进程** | `sessions` (子Agent), `process` | 后台子Agent + 进程管理 |
| **通信** | `message` | 通过 Slack/Webhook 发送消息 |
| **画布** | `canvas` | HTML/SVG 内容渲染 |
| **定时** | `cron` | 定时任务调度 |
| **多媒体** | `image`, `tts` | 图片生成 + 文字转语音 |
| **网关** | `gateway` | MCP Server / OpenAPI 外部集成 |

### 2.3 工具管道

```
Tool定义 → AnyAgentTool接口适配 → 安全过滤(allowlist) → 注入ChatClient
```

Plugin 可以贡献额外的 tools，通过 allowlist 做安全控制。

---

## 三、Skills（知识指令层）— 核心机制

### 3.1 Skill 文件结构

```
skill-name/
├── SKILL.md          (必需) — YAML frontmatter + Markdown 正文
├── scripts/          (可选) — 可执行脚本（Python/Bash）
├── references/       (可选) — 待按需加载的参考文档
└── assets/           (可选) — 模板/图片等输出资源
```

**SKILL.md 格式示例**：

```yaml
---
name: github
description: "Interact with GitHub using `gh` CLI. Use `gh issue`, `gh pr`..."
metadata:
  openclaw:
    emoji: "🐙"
    requires:
      bins: ["gh"]             # PATH中必须有这些二进制
    install:
      - id: brew
        kind: brew             # 安装方式: brew/apt/node/go/uv/download
        formula: gh
        bins: ["gh"]
---
```

正文是纯 Markdown，教 LLM 如何用已有工具（如 `exec`）来执行 `gh` 命令。

### 3.2 实际 Skill 示例

| Skill | 描述 | 依赖 | 工作原理 |
|-------|------|------|----------|
| **github** | GitHub 操作 | `bins: ["gh"]` | 教 LLM 使用 `exec` 工具运行 `gh pr`, `gh issue` 等命令 |
| **weather** | 天气查询 | `bins: ["curl"]` | 教 LLM 用 `exec` 调用 `curl wttr.in` 和 Open-Meteo API |
| **slack** | Slack 操控 | `config: ["channels.slack"]` | 教 LLM 使用内建 `slack` 工具的 JSON action 格式 |
| **coding-agent** | 调度子Agent | `anyBins: ["claude","codex","pi"]` | 教 LLM 用 `exec` + `process` 管理后台 Codex/Claude 实例 |
| **skill-creator** | 创建新Skill | 无 | 元技能 — 教 LLM 如何编写 SKILL.md |

### 3.3 六级目录优先级

```
加载优先级（低→高）：
1. extra/           — 额外扩展
2. bundled/         — 内建随发行版
3. managed/         — 远程安装
4. personal-agents/ — 用户全局自定义
5. project-agents/  — 项目级(.agents目录)
6. workspace/       — 工作区级(最高优先级)
```

同名 Skill 高优先级覆盖低优先级。

### 3.4 环境门控（Eligibility Gating）

| 门控条件 | 含义 | 示例 |
|----------|------|------|
| `bins` | PATH中必须存在*所有*二进制 | `bins: ["gh"]` |
| `anyBins` | 任一二进制存在即可 | `anyBins: ["claude","codex","pi"]` |
| `env` | 环境变量必须设置 | `env: ["OPENAI_API_KEY"]` |
| `config` | 配置路径必须存在 | `config: ["channels.slack"]` |
| `platform` | 操作系统匹配 | `platform: "darwin"` |

不满足条件的 Skill 不会加载到 Agent 中。

### 3.5 渐进式披露（三级加载）

```
┌──────────────────────────────────────────────────────────────┐
│ Level 1: 始终在上下文中（~100 tokens/skill）               │
│   • name + description (YAML frontmatter)                    │
│   → LLM 看到所有可用 Skill 的摘要列表                        │
├──────────────────────────────────────────────────────────────┤
│ Level 2: Skill 被触发时加载（<5k words）                    │
│   • SKILL.md 正文 (Markdown 指令)                           │
│   → LLM 用 read 工具读取完整 SKILL.md                       │
├──────────────────────────────────────────────────────────────┤
│ Level 3: 按需加载（无限制）                                  │
│   • references/, scripts/, assets/                           │
│   → LLM 根据需要读取附属文件                                  │
└──────────────────────────────────────────────────────────────┘
```

**关键设计**：系统提示词只列出 Skill 的 name + description（约 97 字符/skill），不会撑爆上下文窗口。LLM 自主判断何时需要加载完整内容。

---

## 四、CoreSRE 现有架构对比

| 维度 | CoreSRE 现状 | OpenClaw |
|------|-------------|----------|
| **工具注册** | `ToolRegistration` 实体（RestApi / McpServer） | `AnyAgentTool` 接口 + Plugin 贡献 |
| **工具绑定** | Agent 的 `LlmConfig.ToolRefs` (Guid列表) | AllowList 过滤 |
| **工具执行** | `IToolInvoker` 策略模式（REST/MCP） | `execute()` 函数直接调用 |
| **工具发现** | MCP `tools/list` + OpenAPI 解析 | 硬编码 + Plugin 注册 |
| **知识注入** | Agent `SystemPrompt` 字段（单一字符串） | Skills 系统（多个 SKILL.md 按需组合注入） |
| **沙箱** | K8s Pod (`KubernetesSandboxToolProvider`) | 无（直接 exec） |

**关键差距**：CoreSRE 没有 Skills 层。Agent 的领域知识只能硬编码在 `SystemPrompt` 中，无法模块化复用。

---

## 五、CoreSRE 采纳建议

### 5.1 建议采纳

1. ✅ Skill = Markdown 知识模块（name + description + content 三段式）
2. ✅ 渐进式注入（SystemPrompt 仅列摘要，`read_skill` 按需加载正文）
3. ✅ 与现有 ToolRefs 并行的 SkillRefs 绑定机制
4. ✅ Skill 文件包结构（SKILL.md + scripts/ + references/ + assets/）

### 5.2 需适配调整

1. ⚠️ 不采纳文件系统存储，改用 **MinIO (S3)** 对象存储（AppHost 已集成）
2. ⚠️ 不采纳本地二进制依赖检测，改用 **RequiresTools** 关联已注册工具
3. ⚠️ Skill 的 scripts 执行需要 **K8s 持久化沙箱**（当前仅有临时沙箱）

### 5.3 不采纳

1. ❌ 不需要安装机制（brew/apt 等）— 工具通过 ToolRegistration 在线注册
2. ❌ 不需要六级目录优先级 — 数据库 Scope 枚举即可
3. ❌ 不需要 Platform 门控 — K8s Pod 统一 Linux 环境

---

## 六、后续行动

采纳 Skills 系统之前，需先解决以下前置基础设施问题：

1. **文件系统设计** — Agent/Skill 在 S3 + 沙箱中的文件组织结构
2. **持久化沙箱** — 从临时 Pod 升级为可管理的有状态沙箱
3. **沙箱终端** — 用户可直接向沙箱执行命令的 Web Terminal

这些基础设施就绪后，Skills 系统才能真正发挥作用。

---

*报告完毕。详细实现设计见对应的需求文档与设计文档。*
