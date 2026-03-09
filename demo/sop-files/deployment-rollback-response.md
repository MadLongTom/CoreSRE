# SOP: 部署回滚应急响应

## 适用条件

- 告警名称: HighErrorRate
- 触发条件: HTTP 5xx 错误率急剧上升 (超过 30%) 且与近期部署相关
- 适用服务: demo-app 命名空间下的所有微服务
- 严重级别: P1
- 特征: 最近有 Deployment rollout，且错误率在部署后急剧上升

## 初始化上下文

- Metrics: sum(rate(http_requests_total{namespace="${namespace}", status=~"5.."}[2m])) by (app) / sum(rate(http_requests_total{namespace="${namespace}"}[2m])) by (app) | 各服务错误率 | lookback=15m
- Metrics: sum(rate(http_requests_total{namespace="${namespace}"}[2m])) by (app) | 各服务 QPS | lookback=15m
- Logs: {namespace="${namespace}"} |= "error" | 错误日志 | lookback=5m
- Deployment: pods/${namespace} | Pod 状态

## 处置步骤

### Step 1: 确认错误率是否异常飙升

查询各服务的 HTTP 5xx 错误率:

```promql
sum(rate(http_requests_total{namespace="demo-app", status=~"5.."}[2m])) by (app)
/
sum(rate(http_requests_total{namespace="demo-app"}[2m])) by (app)
```

**判定标准**:
- 正常 baseline: payment-service ~3%, order-service ~5%, inventory-service ~2%
- 如果某个服务错误率 **超过 30%**，说明出现严重异常（非内建随机故障）
- 如果错误率在正常范围内（<10%），无需回滚，按常规 SOP 处理

**预期结果**: 确认 payment-service 或其他服务错误率远超正常 baseline
**超时**: 30 秒

### Step 2: 确认与近期部署的关联

检查错误日志，分析错误模式是否为新引入的问题:

```logql
{namespace="demo-app", app="payment-service"} |= "error"
```

**分析要点**:
- 错误消息是否与之前不同 → 新 bug
- 错误频率是否在短时间内暴增 → 部署引起
- 是否多个 Pod 同时出错 → 代码问题（非单 Pod 故障）

**预期结果**: 确认是代码级别的系统性错误（所有 Pod 都报错）
**超时**: 30 秒

### Step 3: 执行部署回滚

确认错误率 >30% 且为系统性错误后，**立即执行回滚**:

使用 `rollback_deployment` 工具回滚受影响的服务:
- namespace: demo-app
- deployment: <受影响的服务名> (如 payment-service)
- revision: 0 (回滚到上一个版本)

⚠️ 这是一个需要审批的破坏性操作，系统会自动发起审批请求。

**预期结果**: 部署回滚到上一个正常版本
**超时**: 60 秒

### Step 4: 验证恢复

回滚完成后，等待 1-2 分钟让新 Pod 启动，然后验证:

```promql
sum(rate(http_requests_total{namespace="demo-app", status=~"5.."}[2m])) by (app)
/
sum(rate(http_requests_total{namespace="demo-app"}[2m])) by (app)
```

**判定标准**:
- payment-service 错误率应恢复到 ~3% (内建 baseline)
- order-service 错误率应恢复到 ~5%

**预期结果**: 服务错误率恢复到正常 baseline
**超时**: 120 秒

### Step 5: 生成事件总结

汇总本次事件:
1. **根因**: 近期部署引入代码 Bug，导致 payment-service 错误率从 ~3% 飙升至 ~80%
2. **影响范围**: payment-service 及依赖它的 order-service
3. **修复方式**: 通过 `kubectl rollout undo` 回滚到上一个正常版本
4. **恢复时间**: 从发现到恢复约 X 分钟
5. **后续建议**: 排查引入 Bug 的代码变更，补充支付模块的单测覆盖率

## 回退计划

回滚本身就是回退操作。如果回滚后仍未恢复:
1. 检查回滚目标版本是否正确
2. 考虑手动缩容受影响服务: scale_deployment → 0 副本
3. 升级为 P0 事件，通知 Team Agent 深度分析

## 参考资料

- `architecture.md` — 系统架构和服务依赖关系
- `observability-queries.md` — PromQL/LogQL 查询手册
- `troubleshooting-guide.md` — 故障排查指南
