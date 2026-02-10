# Quickstart: 005-frontend-pages

> 前端管理页面快速验证步骤。
> 前置条件：后端 SPEC-001 / SPEC-003 已实现并可运行。

## 前置检查

```bash
# 1. 确认后端运行
curl http://localhost:5156/health
# Expected: Healthy

# 2. 确认前端依赖已安装
cd Frontend
npm install

# 3. 确认新增依赖已安装
npm list react-router react-hook-form zod
```

## 启动验证

```bash
# 启动前端开发服务器
cd Frontend
npm run dev
# Expected: Vite 启动于 http://localhost:5173
```

## 功能验证清单

### 1. 路由 & 布局 (US6)

- [ ] 访问 `http://localhost:5173` → 重定向到 `/agents`
- [ ] 页面包含侧边栏导航（Agent 列表、注册、搜索链接）
- [ ] 侧边栏当前路由高亮
- [ ] 访问 `/nonexistent` → 显示 404 页面

### 2. Agent 列表 (US1)

- [ ] 访问 `/agents` → 显示 Agent 列表
- [ ] 列表显示 Name、Type（Badge）、Status（Badge）、创建时间
- [ ] 类型筛选下拉：全部 / A2A / ChatClient / Workflow
- [ ] 选择类型筛选 → 列表更新
- [ ] 空列表显示"暂无 Agent"空状态
- [ ] 点击 Agent 行 → 跳转 `/agents/{id}`
- [ ] API 错误 → 显示错误消息

### 3. Agent 注册 (US2)

- [ ] 访问 `/agents/new` → 显示注册表单
- [ ] 表单包含：名称、描述、类型选择
- [ ] 选择 A2A → 显示 Endpoint + AgentCard 区域
- [ ] 选择 ChatClient → 显示 LlmConfig 区域
- [ ] 选择 Workflow → 显示 WorkflowRef 字段
- [ ] 提交空表单 → 显示验证错误
- [ ] 填写必填字段并提交 → 创建成功 → 跳转到详情页
- [ ] 重复名称 → 显示 409 冲突错误

### 4. Agent 详情 / 编辑 (US3)

- [ ] 访问 `/agents/{id}` → 显示 Agent 详情
- [ ] 详情页显示所有字段（名称、描述、类型、状态、端点、创建/更新时间）
- [ ] AgentCard 区域显示 Skills、Interfaces、SecuritySchemes
- [ ] LlmConfig 区域显示 ModelId、Instructions
- [ ] 点击"编辑" → 切换到编辑模式（字段可编辑，AgentType 不可改）
- [ ] 修改名称并保存 → 更新成功 → 回到详情模式
- [ ] 验证错误 → 显示字段级错误

### 5. Agent 删除 (US4)

- [ ] 详情页点击"删除" → 弹出确认对话框
- [ ] 对话框显示 Agent 名称
- [ ] 点击"取消" → 关闭对话框
- [ ] 点击"确认删除" → 删除成功 → 跳转到列表页

### 6. Agent 搜索 (US5)

- [ ] 访问 `/agents/search` → 显示搜索页
- [ ] 输入搜索关键词 → 停止输入 300ms 后触发搜索
- [ ] 搜索结果显示 Agent 名称、类型、匹配技能（高亮）、相似度分数
- [ ] 空搜索结果 → 显示"未找到匹配的 Agent"
- [ ] 点击搜索结果 → 跳转到 Agent 详情页

## 构建验证

```bash
cd Frontend
npm run build
# Expected: 0 errors, 0 TypeScript errors

npx tsc --noEmit
# Expected: 0 errors
```

## 注意事项

- 后端需要有测试数据才能验证列表/详情/搜索功能
- 可通过 POST /api/agents 手动创建测试 Agent
- Vite proxy 配置确保 `/api` 请求转发到后端
