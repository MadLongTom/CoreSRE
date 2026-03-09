# 常见故障模式与修复指南

本文档记录 Demo-App 系统中已知的故障模式、根因和标准修复方法，
供 SRE Agent 在执行 SOP 时参考。

## 故障模式 1: HighErrorRate（HTTP 5xx 错误率飙升）

### 典型根因

1. **payment-service 网关超时**
   - 表现: payment-service 的 `payments_total{status="failed"}` 计数上升
   - 日志: `"Payment gateway timeout"`
   - 影响: order-service 调用 payment 失败 → order 返回 500
   - 这是系统内建的 3% 随机故障率，属于正常抖动

2. **inventory-service 库存不足**
   - 表现: inventory-service 的 `http_requests_total{status="500"}` 上升
   - 日志: `"Insufficient stock for SKU-xxx"`
   - 影响: order-service 调用 inventory 失败 → order 返回 500
   - 这是系统内建的 2% 随机故障率

3. **级联失败**
   - 当下游服务（payment 或 inventory）大面积不可用时
   - order-service 所有 POST /api/orders 请求均失败
   - 错误率可能从正常的 ~5% 飙升到 50%+

### 排查步骤

1. 查看各服务的错误率，确定哪个服务是源头
2. 查看源头服务的日志中的具体错误信息
3. 通过 Jaeger 追踪确认调用链中哪个环节失败
4. 检查 Pod 状态是否正常（是否有 OOMKill/CrashLoopBackOff）

### 修复方法

- 如果是随机故障率（<5%）→ 属于正常现象，无需介入
- 如果超过正常阈值 → 检查 Pod 是否健康，必要时重启部署:
  ```
  kubectl rollout restart deployment/<service-name> -n demo-app
  ```

---

## 故障模式 2: HighLatency（P99 延迟 > 1 秒）

### 典型根因

1. **order-service 串行调用**
   - order-service 先调 payment 再调 inventory，延迟累加
   - payment 正常延迟 20~150ms + inventory 正常延迟 10~80ms
   - 总延迟通常 50~280ms（含 order 自身 10~50ms）

2. **单个下游服务响应慢**
   - 查看 payment 和 inventory 各自的 P99 延迟
   - 如果某服务延迟异常 → 可能 Pod 资源不足

3. **K8s 节点资源争用**
   - Pod CPU 被限流（throttling）
   - 检查 `container_cpu_cfs_throttled_periods_total`

### 排查步骤

1. 查看 order-service P99 延迟趋势
2. 分别查看 payment / inventory 的 P99 延迟
3. 通过 Jaeger 找到慢请求，分析各 span 的耗时分布
4. 检查 Pod 资源使用情况

### 修复方法

- 如果单服务延迟高 → 检查 Pod 资源限制，考虑扩容
- 如果全局延迟高 → 检查节点负载，考虑分散调度

---

## 故障模式 3: ServiceDown（服务不可用）

### 典型根因

1. **Pod CrashLoopBackOff**
   - Python 进程崩溃后反复重启
   - 查看 `kubectl describe pod` 的 Events
   - 查看 Loki 中最后的错误日志

2. **Pod Pending**
   - 资源不足无法调度
   - 查看 K8s Events 中的调度失败原因

3. **Readiness Probe 失败**
   - `/health` 端点返回非 200
   - 流量被从 Service 端点摘除

### 排查步骤

1. 确认哪个服务的 `up` 指标为 0
2. 查看该服务的 Pod 状态
3. 查看日志中是否有启动失败的原因
4. 检查 K8s Events

### 修复方法

- CrashLoopBackOff → 检查日志定位 crash 原因，修复后重新部署
- Pending → 释放资源或添加节点
- 临时恢复 → `kubectl rollout restart deployment/<service> -n demo-app`
