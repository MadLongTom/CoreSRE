# CoreSRE 项目宪法 (Project Constitution)

> 本文档是 CoreSRE 项目的最高开发准则，所有贡献者（包括 AI 辅助编码）必须严格遵守。

---

## 第一章 · 核心原则

### 第 1 条 — 三驾马车

本项目同时奉行以下三种开发方法论，三者不可偏废：

| 方法论 | 英文 | 核心理念 |
|--------|------|----------|
| **测试驱动开发** | Test-Driven Development (TDD) | 测试先行，代码后写 |
| **领域驱动设计** | Domain-Driven Design (DDD) | 业务领域模型驱动架构 |
| **规约驱动开发** | Spec-Driven Development (SDD) | 接口契约先于实现 |

### 第 2 条 — 不可违反的铁律

1. **测试不可事后修改** — 测试一旦通过评审/提交，只许新增，不许修改或删除已有断言。
2. **接口先于实现** — 任何功能必须先定义接口契约，再编写实现。
3. **领域模型是唯一真相源** — 所有业务逻辑必须存在于 Domain 层，其他层不得包含业务规则。

---

## 第二章 · 开发工作流（严格顺序）

每个功能/变更必须严格按以下 **五步流程** 执行，不得跳步、乱序：

```
┌─────────────────────────────────────────────────────────┐
│  Step 1: Spec（规约）                                    │
│  ↓                                                      │
│  Step 2: Test（测试）                                    │
│  ↓                                                      │
│  Step 3: Interface（接口）                               │
│  ↓                                                      │
│  Step 4: Implementation（实现）                          │
│  ↓                                                      │
│  Step 5: Verify（验证） — 禁止回头修改 Step 1 & 2        │
│  ✕ 失败 → 只能修改 Step 4（实现），绝不改测试            │
└─────────────────────────────────────────────────────────┘
```

### Step 1 — 编写规约 (Spec)

- 在开始编码前，用自然语言或结构化格式描述功能需求。
- 规约应包含：**输入、输出、边界条件、异常场景**。
- 规约文件放置在 `docs/specs/` 目录下。
- 格式：`SPEC-{编号}-{功能名}.md`

```markdown
# SPEC-001: 用户注册

## 输入
- 用户名 (string, 3-50字符, 唯一)
- 邮箱 (string, 合法邮箱格式, 唯一)
- 密码 (string, 最少8字符, 含大小写+数字)

## 输出
- 成功: 返回用户ID + 201 Created
- 失败: 返回错误详情 + 对应状态码

## 边界条件
- 用户名已存在 → 409 Conflict
- 邮箱格式非法 → 400 Bad Request
- 密码强度不足 → 400 Bad Request
```

### Step 2 — 编写测试 (Test)

- 根据 Step 1 的规约，编写 **全部** 测试用例。
- 测试必须覆盖：正常路径、边界条件、异常场景。
- 测试此时 **必须失败**（Red phase）。
- **⚠️ 此步骤完成后，测试代码即被锁定，后续不允许修改。**

```
测试命名规范:
  {被测方法}_{场景描述}_{期望结果}

示例:
  RegisterUser_WithValidInput_ReturnsCreated
  RegisterUser_WithDuplicateEmail_ReturnsConflict
  RegisterUser_WithWeakPassword_ReturnsBadRequest
```

### Step 3 — 定义接口 (Interface)

- 根据测试的调用方式，定义 Domain 层接口和 Application 层接口。
- 接口应与测试中的 mock/stub 签名完全一致。
- 接口放在 `CoreSRE.Domain/Interfaces/` 或 `CoreSRE.Application/Interfaces/`。

### Step 4 — 编写实现 (Implementation)

- 针对 Step 3 的接口编写具体实现。
- 实现放在对应分层中（Domain 逻辑在 Domain 层，基础设施在 Infrastructure 层）。
- 编写过程中持续运行测试，直到所有测试通过（Green phase）。
- **如果测试失败，只能修改实现代码，绝不修改测试。**

### Step 5 — 验证与重构 (Verify & Refactor)

- 运行全部测试套件，确保通过。
- 可进行代码重构（Refactor phase），但：
  - ✅ 可以重构实现代码
  - ✅ 可以重构测试的辅助方法（Setup/Teardown/Helper）
  - ❌ 不可修改测试的断言逻辑 (Assert)
  - ❌ 不可修改测试的输入数据
  - ❌ 不可删除任何测试用例

---

## 第三章 · DDD 分层架构规则

```
┌──────────────────────────────────────────┐
│              API Layer                   │  ← 路由、DTO 映射、HTTP 关注点
│          (CoreSRE - Minimal API)         │
├──────────────────────────────────────────┤
│          Application Layer               │  ← 用例编排、CQRS、验证
│       (CoreSRE.Application)              │
├──────────────────────────────────────────┤
│           Domain Layer                   │  ← 实体、值对象、领域服务、接口定义
│         (CoreSRE.Domain)                 │
├──────────────────────────────────────────┤
│        Infrastructure Layer              │  ← 数据库、外部服务、接口实现
│      (CoreSRE.Infrastructure)            │
└──────────────────────────────────────────┘
```

### 第 3 条 — 依赖方向

依赖只能从外向内，不可反向：

```
API → Application → Domain ← Infrastructure
```

- Domain 层 **零外部依赖**，不引用任何其他项目。
- Infrastructure 引用 Domain（实现其接口），但 Domain 不知道 Infrastructure 的存在。
- Application 引用 Domain，编排用例逻辑。
- API 引用 Application 和 Infrastructure（仅用于 DI 注册）。

### 第 4 条 — 各层职责边界

| 层 | 允许 | 禁止 |
|----|------|------|
| **Domain** | 实体、值对象、聚合根、领域事件、领域服务、仓储接口 | 引用任何外部包、数据库相关代码、HTTP 相关代码 |
| **Application** | Command/Query、Handler、DTO、验证器、接口编排 | 直接访问数据库、包含业务规则、引用 Infrastructure |
| **Infrastructure** | 仓储实现、DbContext、外部 API 调用、消息队列 | 包含业务逻辑、定义业务接口 |
| **API** | 路由定义、请求/响应映射、中间件配置、DI 注册 | 包含业务逻辑、直接使用 DbContext |

### 第 5 条 — 领域模型规则

1. 实体必须通过 **工厂方法** 或 **构造函数** 创建，保证始终处于有效状态。
2. 值对象必须是 **不可变的** (immutable)。
3. 聚合根是外部访问聚合内部实体的 **唯一入口**。
4. 领域事件用于聚合间通信，禁止聚合间直接引用。

---

## 第四章 · 测试规则

### 第 6 条 — 测试分类与位置

```
Tests/
├── CoreSRE.Domain.Tests/          # 领域层单元测试
├── CoreSRE.Application.Tests/     # 应用层单元测试（Mock 仓储）
├── CoreSRE.Infrastructure.Tests/  # 基础设施集成测试
└── CoreSRE.Api.Tests/             # API 端到端测试
```

### 第 7 条 — 测试覆盖要求

| 类型 | 最低覆盖率 | 说明 |
|------|-----------|------|
| Domain 层 | **95%** | 所有业务规则必须有测试 |
| Application 层 | **90%** | 所有 Handler 必须有测试 |
| Infrastructure 层 | **80%** | 仓储实现需集成测试 |
| API 层 | **80%** | 主要路由需端到端测试 |

### 第 8 条 — 测试锁定规则（不可违反）

```
一旦测试代码被提交（commit），以下操作被永久禁止：

  ❌ 修改已有测试的 Assert 断言
  ❌ 修改已有测试的输入参数/测试数据
  ❌ 删除已有测试用例
  ❌ 将失败的测试标记为 [Skip] / [Ignore]
  ❌ 修改测试使其适配实现

以下操作被允许：

  ✅ 新增测试用例
  ✅ 重构测试的 Setup/Helper 方法（不影响断言）
  ✅ 改进测试的可读性（变量重命名等，不改变语义）
```

### 第 9 条 — 当测试失败时

```
测试失败
  │
  ├─ 测试本身有 Bug？
  │   └─ 不可能。测试是基于规约编写的，规约即真理。
  │      如果规约错了，需走「规约变更流程」（见第五章）。
  │
  └─ 实现有 Bug
      └─ 修改 Step 4（实现代码），直到测试通过。
```

---

## 第五章 · 变更管理

### 第 10 条 — 规约变更流程

当业务需求变化导致原有规约不再适用时：

1. 编写 **新的规约**（不修改旧规约，标记旧规约为 `SUPERSEDED`）。
2. 基于新规约编写 **新的测试**。
3. 旧测试标记为 `[Obsolete]` 并附上新规约编号（不删除）。
4. 按 Step 3-5 重新实现。

### 第 11 条 — 版本化规约

```
docs/specs/
├── SPEC-001-user-registration.md          # 状态: ACTIVE
├── SPEC-001-user-registration.v2.md       # 状态: ACTIVE (取代 v1 的部分条款)
└── SPEC-002-user-authentication.md        # 状态: ACTIVE
```

---

## 第六章 · 命名规范

### 第 12 条 — 项目与文件

| 类型 | 规范 | 示例 |
|------|------|------|
| 项目名 | `CoreSRE.{层名}` | `CoreSRE.Domain` |
| 测试项目 | `CoreSRE.{层名}.Tests` | `CoreSRE.Domain.Tests` |
| 实体 | PascalCase 名词 | `User`, `Incident` |
| 值对象 | PascalCase 名词 | `EmailAddress`, `Password` |
| 接口 | `I` + PascalCase | `IUserRepository` |
| Command | `{动词}{名词}Command` | `RegisterUserCommand` |
| Query | `Get{名词}Query` | `GetUserByIdQuery` |
| Handler | `{Command/Query}Handler` | `RegisterUserCommandHandler` |
| Validator | `{Command/Query}Validator` | `RegisterUserCommandValidator` |

### 第 13 条 — 前端规范

| 类型 | 规范 | 示例 |
|------|------|------|
| 组件 | PascalCase | `UserProfile.tsx` |
| Hook | camelCase, `use` 前缀 | `useAuth.ts` |
| 工具函数 | camelCase | `formatDate.ts` |
| 类型定义 | PascalCase | `User.ts` |
| API 调用 | camelCase, `api` 前缀 | `apiGetUsers.ts` |

---

## 第七章 · AI 辅助开发规则

### 第 14 条 — AI 协作约束

当使用 AI（如 GitHub Copilot）辅助开发时：

1. **AI 必须遵守本宪法的所有条款**，无一例外。
2. AI 生成代码时，必须按照第二章的五步流程执行。
3. AI 不得以「简化」「效率」为由跳过任何步骤。
4. AI 不得修改已有测试，即使认为测试有误。
5. 如果 AI 发现测试与规约矛盾，应当**报告冲突**，由人工决定。

### 第 15 条 — AI 输出检查清单

AI 每次提交代码前，需自行验证：

```
□ 是否先编写/确认了规约？
□ 是否先编写了测试？
□ 测试是否当前处于失败状态（Red）？
□ 是否先定义了接口？
□ 实现是否仅在对应分层中？
□ 是否所有测试通过（Green）？
□ 是否有修改已有测试？→ 如是，立即撤回。
```

---

## 第八章 · 目录结构总览

```
CoreSRE/
├── docs/
│   └── specs/                          # 规约文档
│       └── SPEC-XXX-feature.md
├── Backend/
│   ├── CoreSRE/                        # API 层 (Minimal API)
│   │   ├── Endpoints/                  # 路由端点
│   │   ├── Middleware/                  # 中间件
│   │   └── Program.cs
│   ├── CoreSRE.Domain/                 # 领域层
│   │   ├── Entities/                   # 实体
│   │   ├── ValueObjects/               # 值对象
│   │   ├── Interfaces/                 # 仓储等接口
│   │   ├── Services/                   # 领域服务
│   │   └── Events/                     # 领域事件
│   ├── CoreSRE.Application/           # 应用层
│   │   ├── Commands/                   # 命令 (CQRS)
│   │   ├── Queries/                    # 查询 (CQRS)
│   │   ├── Common/                     # 公共组件
│   │   └── DTOs/                       # 数据传输对象
│   ├── CoreSRE.Infrastructure/        # 基础设施层
│   │   ├── Persistence/                # 数据持久化
│   │   ├── Services/                   # 外部服务实现
│   │   └── Configurations/             # EF 实体配置
│   └── Tests/
│       ├── CoreSRE.Domain.Tests/
│       ├── CoreSRE.Application.Tests/
│       ├── CoreSRE.Infrastructure.Tests/
│       └── CoreSRE.Api.Tests/
├── Frontend/                           # React + shadcn/ui
│   ├── src/
│   │   ├── components/
│   │   ├── hooks/
│   │   ├── lib/
│   │   ├── pages/
│   │   └── types/
│   └── ...
├── Makefile                            # 快捷命令
├── dev.ps1                             # 一键启动脚本
└── CONSTITUTION.md                     # 本文件 (项目宪法)
```

---

## 附录 · 违宪处理

| 违反条款 | 后果 |
|----------|------|
| 修改已有测试断言 | **立即 revert**，要求重新走变更流程 |
| 跳过测试直接写实现 | **代码不予合并**，补写测试后重新提交 |
| 在 Domain 层引入外部依赖 | **立即修复**，迁移至 Infrastructure 层 |
| 业务逻辑写在 API 层 | **立即重构**，下沉到 Domain/Application 层 |
| AI 未遵守五步流程 | **撤回 AI 生成的代码**，按流程重来 |

---

*本宪法自项目创建之日起生效，修改需经全体核心成员一致同意。*

*最后更新: 2026-02-09*
