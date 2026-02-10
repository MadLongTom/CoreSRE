# Quickstart: LLM Provider 配置与模型发现

**Feature**: 006-llm-provider-config | **Date**: 2026-02-10

## 验证清单

### US1: 注册 LLM Provider ✅

1. 打开 Provider 管理页面
2. 点击 "Add Provider" 按钮
3. 填写表单：Name = "Ollama Local", Base URL = "http://localhost:11434/v1", API Key = "ollama"
4. 点击提交
5. ✅ 验证：页面跳转到 Provider 列表，新 Provider 出现在列表中
6. ✅ 验证：列表中显示 Provider 名称和 Base URL
7. ✅ 验证：API Key 不在列表中显示
8. 尝试使用相同名称再次创建
9. ✅ 验证：收到 409 冲突错误提示

### US2: 发现可用模型 ✅

> 前提：已有一个 Provider（如 Ollama 本地运行中）

1. 在 Provider 列表点击进入 Provider 详情
2. 点击 "Discover Models" / "Refresh Models" 按钮
3. ✅ 验证：显示 loading 状态
4. ✅ 验证：发现完成后，模型列表显示可用模型（如 llama3.2:latest）
5. ✅ 验证：显示最后刷新时间
6. 测试错误场景：修改 Provider 的 Base URL 为不可达地址，再次 Discover
7. ✅ 验证：显示有意义的错误消息（如 "Connection refused"）

### US3: 创建 ChatClient 时选择 Provider 和 Model ✅

> 前提：已有至少一个 Provider 且已发现模型

1. 导航到 Agent 创建页面
2. 选择 "Chat Client" 类型
3. ✅ 验证：LLM Config 区域显示 Provider 下拉选择
4. 选择一个 Provider
5. ✅ 验证：Model 下拉框加载该 Provider 的可用模型列表
6. 选择一个 Model
7. 填写其他必填字段，提交
8. ✅ 验证：Agent 创建成功
9. 查看创建的 Agent 详情
10. ✅ 验证：显示 Provider 名称和选中的 Model

### US4: 编辑和删除 Provider ✅

1. 进入 Provider 列表，点击编辑一个 Provider
2. 修改 Name，不修改 API Key
3. 提交
4. ✅ 验证：Name 更新成功，API Key 保持不变
5. 修改 Name 为已存在的其他 Provider 名称
6. ✅ 验证：收到 409 冲突错误
7. 创建一个新 Provider（无 Agent 引用）
8. 点击删除
9. ✅ 验证：确认弹窗出现
10. 确认删除
11. ✅ 验证：Provider 从列表中移除
12. 尝试删除一个被 Agent 引用的 Provider
13. ✅ 验证：收到 409 错误，提示有 Agent 引用

### US5: 编辑 ChatClient 的 Provider/Model ✅

1. 进入一个现有 ChatClient Agent 的编辑页面
2. ✅ 验证：当前 Provider 和 Model 已被选中
3. 切换 Provider 到另一个
4. ✅ 验证：Model 下拉框更新为新 Provider 的模型列表
5. 选择新 Model，保存
6. ✅ 验证：Agent 更新成功，显示新的 Provider 和 Model

---

## 开发环境准备

```bash
# 后端启动（需要 .NET 10 + Aspire）
cd Backend/CoreSRE
dotnet run

# 前端启动
cd Frontend
npm install
npm run dev
```

## API 快速测试

```bash
# 创建 Provider
curl -X POST http://localhost:5000/api/providers \
  -H "Content-Type: application/json" \
  -d '{"name":"Ollama","baseUrl":"http://localhost:11434/v1","apiKey":"ollama"}'

# 列出所有 Provider
curl http://localhost:5000/api/providers

# 发现模型
curl -X POST http://localhost:5000/api/providers/{id}/discover

# 获取模型列表
curl http://localhost:5000/api/providers/{id}/models
```
