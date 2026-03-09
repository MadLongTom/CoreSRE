# 可观测性查询参考手册

本文档列出 CoreSRE 平台中可用的数据源查询示例，供 SOP 执行和故障排查使用。

## 1. Prometheus 指标查询

### 通用查询

```promql
# 检查所有服务是否在线
up{namespace="demo-app"}

# 查看各服务 QPS
sum(rate(http_requests_total{namespace="demo-app"}[5m])) by (app)
```

### 错误率相关

```promql
# 单服务 5xx 错误率
sum(rate(http_requests_total{namespace="demo-app", app="order-service", status=~"5.."}[5m]))
/
sum(rate(http_requests_total{namespace="demo-app", app="order-service"}[5m]))

# 全局错误率
sum(rate(http_requests_total{namespace="demo-app", status=~"5.."}[5m]))
/
sum(rate(http_requests_total{namespace="demo-app"}[5m]))

# 按服务分组的错误计数
sum(increase(http_requests_total{namespace="demo-app", status=~"5.."}[10m])) by (app)
```

### 延迟相关

```promql
# 某服务 P99 延迟
histogram_quantile(0.99, sum(rate(http_request_duration_seconds_bucket{namespace="demo-app", app="order-service"}[5m])) by (le))

# P95 延迟
histogram_quantile(0.95, sum(rate(http_request_duration_seconds_bucket{namespace="demo-app", app="order-service"}[5m])) by (le))

# 平均延迟
sum(rate(http_request_duration_seconds_sum{namespace="demo-app", app="order-service"}[5m]))
/
sum(rate(http_request_duration_seconds_count{namespace="demo-app", app="order-service"}[5m]))
```

### 业务指标

```promql
# 订单成功/失败比
sum(rate(orders_total{app="order-service"}[5m])) by (status)

# 支付成功率（按网关分组）
sum(rate(payments_total{app="payment-service", status="success"}[5m])) by (gateway)

# 支付失败率
sum(rate(payments_total{app="payment-service", status="failed"}[5m]))

# 库存水位
inventory_level{app="inventory-service"}
```

## 2. Loki 日志查询

### 基础查询（LogQL）

```logql
# 查看某服务所有日志
{namespace="demo-app", app="order-service"}

# 过滤错误日志
{namespace="demo-app", app="order-service"} |= "error"

# 过滤特定 trace_id 的日志
{namespace="demo-app"} | json | trace_id="<具体trace_id>"

# 查看支付失败日志
{namespace="demo-app", app="payment-service"} |= "Payment failed"

# 查看库存不足日志
{namespace="demo-app", app="inventory-service"} |= "Insufficient stock"
```

### 聚合查询

```logql
# 错误日志频率（每分钟）
sum(count_over_time({namespace="demo-app"} |= "error" [1m])) by (app)

# 各服务日志量
sum(count_over_time({namespace="demo-app"} [5m])) by (app)
```

## 3. Jaeger 链路追踪

### 查询维度

- **Service**: order-service, payment-service, inventory-service
- **Operation**: 按 HTTP method + path 自动命名（如 `POST /api/orders`）
- **Duration**: 可按延迟范围筛选慢请求
- **Tags**: 可按 `http.status_code`, `error=true` 等标签筛选

### 排查模式

1. **查找慢请求**: 按 duration 降序排列，找出 P99+ 的请求
2. **查找错误链路**: 按 `error=true` tag 筛选，查看错误在哪个 span 发生
3. **调用链分析**: 展开单条 trace，查看 order → payment / inventory 的调用关系和耗时分布

## 4. Alertmanager 告警

### 已配置的告警规则（Prometheus Rules）

| 规则名 | 触发条件 | 严重级别 |
|--------|---------|---------|
| HighErrorRate | HTTP 5xx 错误率 > 5%，持续 2 分钟 | critical |
| HighLatency | P99 延迟 > 1 秒，持续 3 分钟 | warning |
| ServiceDown | 服务 up == 0，持续 1 分钟 | critical |

### 告警标签

告警发出时携带以下标签：
- `alertname` — 规则名（HighErrorRate / HighLatency / ServiceDown）
- `severity` — 严重程度（critical / warning）
- `namespace` — 命名空间（demo-app）
- `service` — 受影响的服务名

## 5. Kubernetes 资源查询

### 常用查询目标

- `pods/demo-app` — 查看 demo-app 下所有 Pod 状态
- `deployments/demo-app` — 查看 Deployment 副本数和状态
- `events/demo-app` — 查看最近的 K8s 事件（OOMKill, CrashLoopBackOff 等）
