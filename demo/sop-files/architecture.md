# Demo-App 分布式电商系统架构参考

## 系统概述

Demo-App 是一个部署在 Kubernetes `demo-app` namespace 下的分布式电商微服务系统，
用于模拟真实的 SRE 运维场景。系统包含三个核心业务服务和一个流量生成器。

## 服务拓扑

```
                     ┌──────────────────┐
                     │ traffic-generator│
                     │  (持续模拟流量)    │
                     └────────┬─────────┘
                              │
                    POST /api/orders
                              │
                     ┌────────▼─────────┐
                     │  order-service    │
                     │  (2 replicas)     │
                     │  Port: 8080       │
                     └──┬────────────┬──┘
                        │            │
          POST /api/payments    POST /api/inventory/reserve
                        │            │
               ┌────────▼───┐  ┌─────▼──────────┐
               │  payment-  │  │  inventory-     │
               │  service   │  │  service        │
               │ (2 replicas)│  │ (2 replicas)    │
               │ Port: 8080 │  │ Port: 8080      │
               └────────────┘  └─────────────────┘
```

## 各服务详情

### order-service（订单服务）

- **职责**: 接收订单，编排调用 payment-service 和 inventory-service
- **端点**:
  - `POST /api/orders` — 创建订单（调用支付+库存）
  - `GET /api/orders` — 查询订单列表
  - `GET /health` — 健康检查
  - `GET /metrics` — Prometheus 指标
- **关键指标**:
  - `http_requests_total{app="order-service"}` — 按 method/endpoint/status 分类的请求计数
  - `http_request_duration_seconds{app="order-service"}` — 请求延迟分布（直方图）
  - `orders_total{app="order-service"}` — 订单计数（按 status=success/failed）
- **错误场景**: 下游服务（payment/inventory）调用失败时返回 500

### payment-service（支付服务）

- **职责**: 模拟支付处理，随机分配支付网关（stripe/paypal/alipay）
- **端点**:
  - `POST /api/payments` — 处理支付
  - `GET /health` — 健康检查
  - `GET /metrics` — Prometheus 指标
- **关键指标**:
  - `http_requests_total{app="payment-service"}` — 请求计数
  - `http_request_duration_seconds{app="payment-service"}` — 请求延迟
  - `payments_total{app="payment-service"}` — 支付计数（按 status/gateway 分类）
- **内建故障**: **3% 随机失败率**（模拟 "Payment gateway timeout"）
- **延迟特征**: 20ms ~ 150ms 处理时间

### inventory-service（库存服务）

- **职责**: 模拟库存管理，维护 SKU-100 ~ SKU-199 共 100 个商品库存
- **端点**:
  - `POST /api/inventory/reserve` — 库存预留
  - `GET /api/inventory` — 查询库存列表
  - `GET /health` — 健康检查
  - `GET /metrics` — Prometheus 指标
- **关键指标**:
  - `http_requests_total{app="inventory-service"}` — 请求计数
  - `http_request_duration_seconds{app="inventory-service"}` — 请求延迟
  - `inventory_level{app="inventory-service"}` — 实时库存水位（Gauge）
- **内建故障**: **2% 随机缺货率**（模拟 "Insufficient stock"）
- **延迟特征**: 10ms ~ 80ms 处理时间

### traffic-generator（流量发生器）

- **职责**: 持续产生模拟流量，确保可观测性数据持续生成
- **行为**:
  - 每轮发送 2~8 个创建订单请求
  - 30% 概率发送直接支付请求
  - 30% 概率发送直接库存查询
  - 每轮间隔 1~3 秒
  - 每 10 轮打印一条日志

## 可观测性集成

所有服务均集成以下可观测性能力:

| 类型 | 技术 | 采集方式 |
|------|------|---------|
| **Metrics** | prometheus_client (Python SDK) | Prometheus 主动抓取 `/metrics` |
| **Logs** | 结构化 JSON 日志到 stdout | Promtail DaemonSet → Loki |
| **Traces** | OpenTelemetry SDK + OTLP | OTLP HTTP → Jaeger |

### 日志格式
```json
{
  "timestamp": "2026-03-08T10:30:00.000Z",
  "level": "info",
  "msg": "Order created successfully",
  "service": "order-service",
  "logger": "order-service",
  "trace_id": "abc123...",
  "span_id": "def456..."
}
```

### Trace 传播
- order-service 自动向 payment-service 和 inventory-service 传播 trace context
- 使用 OpenTelemetry `urllib` 和 `flask` instrumentation 实现自动注入
- 可在 Jaeger 中查看完整的分布式调用链

## Kubernetes 部署信息

- **Namespace**: `demo-app`
- **每个服务 2 个副本**（单一 Deployment，label: `app=<service-name>`）
- **Service 类型**: ClusterIP（内部通信）
- **资源限制**: 每个 Pod 50m~200m CPU, 128Mi~256Mi Memory
- **就绪/存活探针**: HTTP GET `/health`
