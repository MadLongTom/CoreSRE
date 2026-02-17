# SRE Data Source API Research

> Research-only reference for designing a unified data source abstraction layer in CoreSRE.

---

## Table of Contents

1. [Category 1: Metrics](#1-metrics)
2. [Category 2: Logs](#2-logs)
3. [Category 3: Tracing](#3-tracing)
4. [Category 4: Alerting](#4-alerting)
5. [Category 5: Deployment / CD](#5-deployment--cd)
6. [Category 6: Git / SCM](#6-git--scm)
7. [Cross-Category Comparison Table](#cross-category-comparison-table)
8. [Key Design Insights for Unified Abstraction](#key-design-insights-for-unified-abstraction)

---

## 1. Metrics

### Products

| Product | Notes |
|---|---|
| **Prometheus** | De facto CNCF standard; PromQL; pull-based scrape model |
| **VictoriaMetrics** | Prometheus-compatible API; MetricsQL superset of PromQL |
| **Thanos / Mimir** | Long-term Prometheus storage; same query API surface |
| **Datadog** | SaaS; proprietary query language; REST API |

### Query API Model

**Prometheus (canonical reference — VictoriaMetrics / Thanos / Mimir are wire-compatible)**

| Endpoint | Method | Purpose |
|---|---|---|
| `/api/v1/query` | GET / POST | Instant query (single timestamp) |
| `/api/v1/query_range` | GET / POST | Range query (time series over window) |
| `/api/v1/series` | GET / POST | Find series matching label selectors |
| `/api/v1/labels` | GET / POST | List all known label names |
| `/api/v1/label/<name>/values` | GET | List values for a label |
| `/api/v1/targets` | GET | Active scrape targets |
| `/api/v1/rules` | GET | Alerting & recording rules |
| `/api/v1/alerts` | GET | Active alerts from rules |
| `/api/v1/metadata` | GET | Per-metric metadata (type, help, unit) |

**Key Query Parameters (instant):**

| Param | Type | Description |
|---|---|---|
| `query` | string | PromQL expression |
| `time` | RFC3339 / unix | Evaluation timestamp (default: now) |
| `timeout` | duration | Evaluation timeout |
| `limit` | int | Max number of returned series |

**Key Query Parameters (range):**

| Param | Type | Description |
|---|---|---|
| `query` | string | PromQL expression |
| `start` | RFC3339 / unix | Start timestamp |
| `end` | RFC3339 / unix | End timestamp |
| `step` | duration / float | Query resolution step width |
| `timeout` | duration | Evaluation timeout |
| `limit` | int | Max number of returned series |

### Response Shape

All Prometheus API responses share a common envelope:

```json
{
  "status": "success" | "error",
  "data": { ... },
  "errorType": "<string>",   // only on error
  "error": "<string>",       // only on error
  "warnings": ["<string>"]   // optional
}
```

**Instant query `data`:**

```json
{
  "resultType": "vector" | "matrix" | "scalar" | "string",
  "result": [
    {
      "metric": { "__name__": "up", "job": "prometheus", "instance": "localhost:9090" },
      "value": [1435781451.781, "1"]   // [unix_timestamp, string_value]
    }
  ]
}
```

**Range query `data`:**

```json
{
  "resultType": "matrix",
  "result": [
    {
      "metric": { "__name__": "http_requests_total", "method": "GET" },
      "values": [
        [1435781430.781, "1"],
        [1435781445.781, "2"],
        [1435781460.781, "3"]
      ]
    }
  ]
}
```

**Shape summary:**
- Labels are `Dictionary<string, string>` (metric field)
- Values are always `[unix_timestamp_float, string_value]` tuples
- `resultType` discriminates the union

### Auth Methods

| Method | Context |
|---|---|
| None (default) | Prometheus OSS has no built-in auth |
| Reverse proxy (NGINX/OAuth2-proxy) | Common production pattern |
| Bearer token | Thanos/Mimir/VictoriaMetrics enterprise |
| Basic Auth | Grafana Cloud hosted Prometheus |
| mTLS | Service-mesh or internal setups |

### .NET Client Libraries

| Library | NuGet | Notes |
|---|---|---|
| `Prometheus.Client` | prometheus-net | For *exposing* metrics (instrumentation) |
| `PrometheusQuerySdk` | — | Community; thin HTTP wrapper |
| **Best approach** | `HttpClient` + JSON | API is simple REST; use typed DTOs |

---

## 2. Logs

### Products

| Product | Notes |
|---|---|
| **Grafana Loki** | LogQL; label-indexed, log-chunk storage |
| **Elasticsearch** | Lucene/KQL; full-text indexed; Query DSL |
| **OpenSearch** | Elasticsearch fork; API-compatible |
| **Splunk** | SPL; proprietary; REST search API |

### Query API Model

#### Loki

| Endpoint | Method | Purpose |
|---|---|---|
| `/loki/api/v1/query` | GET | Instant query (LogQL) |
| `/loki/api/v1/query_range` | GET | Range query (LogQL) |
| `/loki/api/v1/labels` | GET | List label names |
| `/loki/api/v1/label/<name>/values` | GET | List values for a label |
| `/loki/api/v1/series` | GET / POST | Series matching selectors |
| `/loki/api/v1/tail` | WebSocket | Stream logs in real-time |
| `/loki/api/v1/push` | POST | Ingest log entries |
| `/loki/api/v1/index/stats` | GET | Index statistics |

**Key Query Parameters (range):**

| Param | Type | Description |
|---|---|---|
| `query` | string | LogQL expression |
| `start` | RFC3339 / unix nano | Start timestamp |
| `end` | RFC3339 / unix nano | End timestamp |
| `since` | duration | Alternative to start (e.g. `5m`) |
| `step` | duration | Metric query step |
| `limit` | int | Maximum number of entries (default 100) |
| `direction` | `forward` / `backward` | Sort order |
| `interval` | duration | Return one entry per interval |

#### Elasticsearch / OpenSearch

| Endpoint | Method | Purpose |
|---|---|---|
| `/<index>/_search` | GET / POST | Full-text search with Query DSL |
| `/<index>/_count` | GET / POST | Count matching documents |
| `/<index>/_msearch` | POST | Multi-search (batch) |
| `/_cat/indices` | GET | List indices |
| `/<index>/_mapping` | GET | Index field mappings |
| `/_search/scroll` | POST | Scroll through large result sets |

**Elasticsearch Query DSL body (POST):**

```json
{
  "query": {
    "bool": {
      "must": [
        { "match": { "message": "error" } },
        { "range": { "@timestamp": { "gte": "2024-01-01", "lte": "2024-01-02" } } }
      ],
      "filter": [
        { "term": { "service.name": "api-gateway" } }
      ]
    }
  },
  "size": 100,
  "from": 0,
  "sort": [{ "@timestamp": "desc" }],
  "_source": ["message", "@timestamp", "level", "service.name"],
  "aggs": {
    "log_levels": {
      "terms": { "field": "level.keyword" }
    }
  }
}
```

### Response Shape

#### Loki

```json
{
  "status": "success",
  "data": {
    "resultType": "streams" | "matrix",
    "result": [
      {
        "stream": { "app": "api", "namespace": "prod" },
        "values": [
          ["1689012345000000000", "level=error msg=\"connection refused\""],
          ["1689012344000000000", "level=info msg=\"request completed\""]
        ]
      }
    ],
    "stats": { "summary": { "bytesProcessedPerSecond": 123456 } }
  }
}
```

**Shape summary:**
- Labels in `stream` field: `Dictionary<string, string>`
- Values: `[nanosecond_unix_epoch_string, log_line_string]`
- For metric queries (`resultType: "matrix"`): same shape as Prometheus matrix

#### Elasticsearch

```json
{
  "took": 12,
  "timed_out": false,
  "_shards": { "total": 5, "successful": 5, "skipped": 0, "failed": 0 },
  "hits": {
    "total": { "value": 10000, "relation": "gte" },
    "max_score": 1.0,
    "hits": [
      {
        "_index": "logs-2024.01.01",
        "_id": "abc123",
        "_score": 1.0,
        "_source": {
          "@timestamp": "2024-01-01T12:00:00Z",
          "message": "connection refused",
          "level": "error",
          "service": { "name": "api-gateway" }
        }
      }
    ]
  },
  "aggregations": {
    "log_levels": {
      "buckets": [
        { "key": "error", "doc_count": 150 },
        { "key": "info", "doc_count": 8500 }
      ]
    }
  }
}
```

### Auth Methods

| Product | Auth Methods |
|---|---|
| **Loki** | `X-Scope-OrgID` header (multi-tenant OSS), Basic Auth (Grafana Cloud), Bearer token |
| **Elasticsearch** | Basic Auth, API Key (`Authorization: ApiKey <encoded>`), Bearer token (OIDC/SAML), PKI/mTLS |
| **OpenSearch** | Same as Elasticsearch + AWS IAM Sig v4 |

### .NET Client Libraries

| Library | NuGet | Notes |
|---|---|---|
| `Elastic.Clients.Elasticsearch` | Official 8.x | Strongly typed Query DSL builder |
| `NEST` (legacy) | Elastic.co | 7.x line; widely adopted |
| `OpenSearch.Client` | opensearch-project | Fork of NEST for OpenSearch |
| Loki | None official | Use `HttpClient` + JSON DTOs |

---

## 3. Tracing

### Products

| Product | Notes |
|---|---|
| **Jaeger** | CNCF graduated; gRPC + HTTP; OTLP ingest |
| **Grafana Tempo** | TraceQL; no indexing (object-store backend) |
| **Zipkin** | Lightweight; JSON/Thrift API |
| **Datadog APM** | SaaS; proprietary API |

### Query API Model

#### Jaeger

| Endpoint | Method | Purpose |
|---|---|---|
| `/api/traces/{traceID}` | GET | Get trace by ID |
| `/api/traces?service=X&operation=Y&start=&end=&limit=` | GET | Search traces |
| `/api/services` | GET | List known services |
| `/api/services/{service}/operations` | GET | List operations for service |
| `/api/dependencies?endTs=&lookback=` | GET | Service dependency graph |

**gRPC (stable):** `jaeger.api_v2.QueryService` on port 16685 — `GetTrace`, `FindTraces`, `GetServices`, `GetOperations`.

**Search parameters:**

| Param | Type | Description |
|---|---|---|
| `service` | string | Service name (required for search) |
| `operation` | string | Operation name filter |
| `tags` | JSON string | Key-value tag filters |
| `start` | microseconds | Start time |
| `end` | microseconds | End time |
| `limit` | int | Max traces returned |
| `minDuration` | duration string | Min span duration (e.g. `1.2s`) |
| `maxDuration` | duration string | Max span duration |

#### Tempo

| Endpoint | Method | Purpose |
|---|---|---|
| `/api/traces/<traceID>` | GET | Get trace by ID |
| `/api/v2/traces/<traceID>` | GET | Get trace by ID (v2) |
| `/api/search?q=<TraceQL>` | GET | Search traces via TraceQL |
| `/api/search/tags` | GET | List available tag names |
| `/api/search/tag/<tag>/values` | GET | List values for a tag |
| `/api/v2/search/tags` | GET | Tag names with scope/type info |
| `/api/v2/search/tag/<tag>/values` | GET | Tag values (v2) |
| `/api/metrics/query_range` | GET | TraceQL-based metrics |

**Search parameters:**

| Param | Type | Description |
|---|---|---|
| `q` | string | TraceQL query (e.g. `{ resource.service.name = "api" && duration > 1s }`) |
| `tags` | logfmt string | Alternative tag-based search |
| `start` | unix epoch seconds | Start of search window |
| `end` | unix epoch seconds | End of search window |
| `limit` | int | Max traces |
| `spss` | int | Spans per span set |

#### Zipkin

| Endpoint | Method | Purpose |
|---|---|---|
| `/api/v2/traces?serviceName=&spanName=&annotationQuery=` | GET | Search traces |
| `/api/v2/trace/{traceId}` | GET | Get trace by ID |
| `/api/v2/services` | GET | List service names |
| `/api/v2/spans?serviceName=` | GET | List span names |
| `/api/v2/dependencies?endTs=&lookback=` | GET | Dependency links |

### Response Shape

#### Jaeger (HTTP JSON)

Jaeger returns traces in a proprietary JSON format:

```json
{
  "data": [
    {
      "traceID": "abc123",
      "spans": [
        {
          "traceID": "abc123",
          "spanID": "def456",
          "operationName": "HTTP GET /api/users",
          "references": [{ "refType": "CHILD_OF", "traceID": "abc123", "spanID": "parent789" }],
          "startTime": 1689012345000000,
          "duration": 15000,
          "tags": [
            { "key": "http.status_code", "type": "int64", "value": 200 },
            { "key": "http.method", "type": "string", "value": "GET" }
          ],
          "logs": [
            { "timestamp": 1689012345100000, "fields": [{ "key": "event", "value": "cache.miss" }] }
          ],
          "processID": "p1"
        }
      ],
      "processes": {
        "p1": {
          "serviceName": "api-gateway",
          "tags": [{ "key": "hostname", "type": "string", "value": "pod-1" }]
        }
      }
    }
  ],
  "total": 1,
  "limit": 0,
  "offset": 0
}
```

#### Tempo

Tempo returns **OpenTelemetry JSON** (OTLP) by default. Search response:

```json
{
  "traces": [
    {
      "traceID": "2f3e4d5c6b7a8091",
      "rootServiceName": "api-gateway",
      "rootTraceName": "HTTP GET /users",
      "startTimeUnixNano": "1689012345000000000",
      "durationMs": 557,
      "spanSets": [
        {
          "spans": [
            {
              "spanID": "1a2b3c4d",
              "startTimeUnixNano": "1689012345000000000",
              "durationNanos": "557000000",
              "attributes": [
                { "key": "http.status_code", "value": { "intValue": "200" } }
              ]
            }
          ],
          "matched": 1
        }
      ]
    }
  ],
  "metrics": { "totalBlocks": 13, "totalBlockBytes": "1234567" }
}
```

Full trace GET returns standard OTLP JSON (`resourceSpans` → `scopeSpans` → `spans`). Also supports `Accept: application/protobuf`.

#### Zipkin

```json
[
  [
    {
      "traceId": "abc123",
      "id": "def456",
      "parentId": "parent789",
      "name": "GET /api/users",
      "timestamp": 1689012345000000,
      "duration": 15000,
      "localEndpoint": { "serviceName": "api-gateway", "ipv4": "10.0.0.1", "port": 8080 },
      "remoteEndpoint": { "serviceName": "user-service" },
      "tags": { "http.status_code": "200", "http.method": "GET" },
      "annotations": [{ "timestamp": 1689012345100000, "value": "cache.miss" }]
    }
  ]
]
```

### Auth Methods

| Product | Auth Methods |
|---|---|
| **Jaeger** | None by default; Bearer token via gRPC metadata; reverse proxy |
| **Tempo** | `X-Scope-OrgID` (multi-tenant), Basic Auth (Grafana Cloud), Bearer token |
| **Zipkin** | None by default; reverse proxy |

### .NET Client Libraries

| Library | NuGet | Notes |
|---|---|---|
| `Jaeger.ApiV2` (gRPC) | — | Generate from protobuf definitions |
| `OpenTelemetry SDK` | opentelemetry-dotnet | For *emitting* traces |
| All query APIs | `HttpClient` + JSON | Best approach for querying |

---

## 4. Alerting

### Products

| Product | Notes |
|---|---|
| **Prometheus Alertmanager** | CNCF; OpenAPI v2 spec; groups, silences, inhibition |
| **PagerDuty** | SaaS; Events API v2 + REST API |
| **Opsgenie** | SaaS (Atlassian); Alert API |
| **Grafana OnCall** | OSS/Cloud; REST API |

### Query API Model

#### Alertmanager API v2

Base path: `/api/v2/`

| Endpoint | Method | Purpose |
|---|---|---|
| `/api/v2/alerts` | GET | List alerts (with filters) |
| `/api/v2/alerts` | POST | Create/fire alerts |
| `/api/v2/alerts/groups` | GET | List alert groups |
| `/api/v2/silences` | GET | List silences |
| `/api/v2/silences` | POST | Create/update silence |
| `/api/v2/silence/{silenceID}` | GET | Get silence by ID |
| `/api/v2/silence/{silenceID}` | DELETE | Delete silence |
| `/api/v2/receivers` | GET | List receivers |
| `/api/v2/status` | GET | Alertmanager status & cluster info |

**GET /api/v2/alerts parameters:**

| Param | Type | Description |
|---|---|---|
| `active` | bool (default true) | Include active alerts |
| `silenced` | bool (default true) | Include silenced alerts |
| `inhibited` | bool (default true) | Include inhibited alerts |
| `unprocessed` | bool (default true) | Include unprocessed alerts |
| `filter` | string[] | Matcher expressions, e.g. `alertname="MyAlert"` |
| `receiver` | string | Regex matching receiver names |

#### PagerDuty

| Endpoint | Method | Purpose |
|---|---|---|
| `POST https://events.pagerduty.com/v2/enqueue` | POST | Trigger/acknowledge/resolve event |
| `GET /incidents` | GET | List incidents |
| `GET /incidents/{id}` | GET | Get incident detail |
| `PUT /incidents/{id}` | PUT | Update incident (ack/resolve) |
| `GET /incidents/{id}/alerts` | GET | List alerts on incident |
| `GET /oncalls` | GET | List on-call entries |
| `GET /services` | GET | List services |
| `GET /escalation_policies` | GET | List escalation policies |

**Events v2 body:**

```json
{
  "routing_key": "<integration_key>",
  "event_action": "trigger" | "acknowledge" | "resolve",
  "dedup_key": "<deduplication_key>",
  "payload": {
    "summary": "CPU > 90% on web-server-01",
    "severity": "critical" | "error" | "warning" | "info",
    "source": "web-server-01",
    "component": "cpu",
    "group": "production",
    "class": "cpu_usage",
    "custom_details": { ... }
  }
}
```

#### Opsgenie

| Endpoint | Method | Purpose |
|---|---|---|
| `POST /v2/alerts` | POST | Create alert |
| `GET /v2/alerts` | GET | List alerts |
| `GET /v2/alerts/{id}` | GET | Get alert |
| `POST /v2/alerts/{id}/close` | POST | Close alert |
| `POST /v2/alerts/{id}/acknowledge` | POST | Acknowledge alert |
| `GET /v2/alerts/{id}/notes` | GET | List alert notes |
| `GET /v2/schedules/on-calls` | GET | On-call schedule |

**GET /v2/alerts parameters:**

| Param | Type | Description |
|---|---|---|
| `query` | string | Search query (e.g. `status=open AND priority=P1`) |
| `searchIdentifier` | string | Alert search identifier |
| `searchIdentifierType` | string | `id`, `tiny`, `alias` |
| `offset` | int | Pagination offset |
| `limit` | int | Page size (max 100) |
| `sort` | string | Sort field |
| `order` | string | `asc` / `desc` |

### Response Shape

#### Alertmanager — Alert object

```json
[
  {
    "annotations": { "summary": "High CPU", "description": "CPU > 90% for 5m" },
    "receivers": [{ "name": "slack-critical" }],
    "fingerprint": "abc123def456",
    "startsAt": "2024-01-01T12:00:00Z",
    "updatedAt": "2024-01-01T12:05:00Z",
    "endsAt": "0001-01-01T00:00:00Z",
    "status": {
      "state": "active" | "suppressed" | "unprocessed",
      "silencedBy": [],
      "inhibitedBy": [],
      "mutedBy": []
    },
    "labels": { "alertname": "HighCPU", "severity": "critical", "instance": "web-01:9090" },
    "generatorURL": "http://prometheus:9090/graph?g0.expr=..."
  }
]
```

#### PagerDuty — Incident object

```json
{
  "incident": {
    "id": "P1234ABC",
    "type": "incident",
    "summary": "CPU > 90%",
    "status": "triggered" | "acknowledged" | "resolved",
    "urgency": "high" | "low",
    "service": { "id": "PSVC123", "summary": "API Gateway" },
    "created_at": "2024-01-01T12:00:00Z",
    "assignments": [{ "assignee": { "id": "PUSER1", "summary": "John Doe" } }],
    "escalation_policy": { "id": "PESC1", "summary": "Engineering" },
    "alert_counts": { "triggered": 1, "resolved": 0, "all": 1 }
  }
}
```

#### Opsgenie — Alert object

```json
{
  "data": {
    "id": "uuid-123",
    "tinyId": "1234",
    "alias": "cpu-alert-web01",
    "message": "CPU > 90%",
    "status": "open" | "closed" | "acked",
    "acknowledged": false,
    "isSeen": false,
    "tags": ["critical", "cpu"],
    "snoozed": false,
    "priority": "P1",
    "owner": "john@example.com",
    "responders": [{ "type": "team", "id": "team-uuid" }],
    "createdAt": "2024-01-01T12:00:00Z",
    "updatedAt": "2024-01-01T12:05:00Z",
    "source": "Prometheus",
    "count": 1,
    "integration": { "id": "integration-uuid", "name": "PrometheusIntegration" }
  }
}
```

### Auth Methods

| Product | Auth Methods |
|---|---|
| **Alertmanager** | None by default; reverse proxy; basic auth via web config |
| **PagerDuty** | API Key (`Authorization: Token token=<key>`), OAuth 2.0 |
| **Opsgenie** | API Key (`Authorization: GenieKey <key>`) |
| **Grafana OnCall** | Bearer token, Grafana service account token |

### .NET Client Libraries

| Library | NuGet | Notes |
|---|---|---|
| `paboroern/pagerduty-dotnet` | — | Community PagerDuty client |
| `Opsgenie.SDK` | — | Community / generated from OpenAPI |
| Alertmanager | `HttpClient` + JSON | Simple REST; OpenAPI v2 spec available for codegen |

---

## 5. Deployment / CD

### Products

| Product | Notes |
|---|---|
| **Argo CD** | GitOps; Kubernetes-native; gRPC + REST; Swagger UI at `/swagger-ui` |
| **Flux CD** | GitOps; Kubernetes CRD-only (no API server); use K8s API |
| **Kubernetes API** | Native; watches, apply, rollout status |
| **Helm** | CLI/library; not a server API |

### Query API Model

#### Argo CD

Base path: `/api/v1/`

| Endpoint | Method | Purpose |
|---|---|---|
| `/api/v1/applications` | GET | List applications |
| `/api/v1/applications/{name}` | GET | Get application detail |
| `/api/v1/applications/{name}` | PUT | Update application |
| `/api/v1/applications/{name}` | DELETE | Delete application |
| `/api/v1/applications/{name}/sync` | POST | Trigger sync |
| `/api/v1/applications/{name}/resource` | GET | Get managed resource |
| `/api/v1/applications/{name}/resource-tree` | GET | Resource tree |
| `/api/v1/applications/{name}/managed-resources` | GET | List managed resources |
| `/api/v1/applications/{name}/events` | GET | Application events |
| `/api/v1/projects` | GET | List projects |
| `/api/v1/clusters` | GET | List clusters |
| `/api/v1/repositories` | GET | List repositories |
| `/api/v1/session` | POST | Create auth session (login) |

**GET /api/v1/applications parameters:**

| Param | Type | Description |
|---|---|---|
| `name` | string | Application name filter |
| `refresh` | string | Force refresh (`normal` or `hard`) |
| `project` | string[] | Project filter |
| `selector` | string | Label selector |
| `repo` | string | Repository URL filter |
| `appNamespace` | string | Application namespace |

#### Kubernetes API (native)

| Endpoint | Method | Purpose |
|---|---|---|
| `/apis/apps/v1/namespaces/{ns}/deployments` | GET | List deployments |
| `/apis/apps/v1/namespaces/{ns}/deployments/{name}` | GET | Get deployment |
| `/apis/apps/v1/namespaces/{ns}/deployments/{name}` | PATCH | Patch deployment |
| `/api/v1/namespaces/{ns}/pods` | GET | List pods |
| `/api/v1/namespaces/{ns}/events` | GET | List events |
| `/apis/apps/v1/namespaces/{ns}/replicasets` | GET | List replica sets |
| Any endpoint + `?watch=true` | GET | Server-Sent Events (watch) |

**Common K8s query parameters:**

| Param | Type | Description |
|---|---|---|
| `labelSelector` | string | e.g. `app=nginx,version=v2` |
| `fieldSelector` | string | e.g. `status.phase=Running` |
| `limit` | int | Page size |
| `continue` | string | Pagination token |
| `watch` | bool | Stream changes |
| `resourceVersion` | string | Watch starting point |
| `timeoutSeconds` | int | Watch timeout |

#### Flux CD

Flux has **no standalone API server**. All interactions go through the Kubernetes API using CRDs:

| CRD | Group | Purpose |
|---|---|---|
| `Kustomization` | `kustomize.toolkit.fluxcd.io/v1` | Declarative deploy unit |
| `HelmRelease` | `helm.toolkit.fluxcd.io/v2` | Helm chart release |
| `GitRepository` | `source.toolkit.fluxcd.io/v1` | Git source reference |
| `HelmRepository` | `source.toolkit.fluxcd.io/v1` | Helm chart repo source |

Query via standard K8s API: `GET /apis/kustomize.toolkit.fluxcd.io/v1/namespaces/{ns}/kustomizations`

### Response Shape

#### Argo CD — Application object

```json
{
  "metadata": {
    "name": "my-app",
    "namespace": "argocd",
    "labels": { "team": "platform" },
    "createdTimestamp": "2024-01-01T00:00:00Z"
  },
  "spec": {
    "source": {
      "repoURL": "https://github.com/org/repo.git",
      "path": "k8s/overlays/prod",
      "targetRevision": "main"
    },
    "destination": {
      "server": "https://kubernetes.default.svc",
      "namespace": "production"
    },
    "project": "default",
    "syncPolicy": { "automated": { "prune": true, "selfHeal": true } }
  },
  "status": {
    "sync": {
      "status": "Synced" | "OutOfSync",
      "revision": "abc123def"
    },
    "health": {
      "status": "Healthy" | "Degraded" | "Progressing" | "Missing" | "Suspended" | "Unknown"
    },
    "operationState": {
      "phase": "Succeeded" | "Running" | "Failed" | "Error",
      "startedAt": "2024-01-01T12:00:00Z",
      "finishedAt": "2024-01-01T12:01:30Z",
      "message": "successfully synced"
    },
    "resources": [
      {
        "group": "apps",
        "version": "v1",
        "kind": "Deployment",
        "namespace": "production",
        "name": "my-app",
        "status": "Synced",
        "health": { "status": "Healthy" }
      }
    ]
  }
}
```

#### Kubernetes — Deployment object (condensed)

```json
{
  "apiVersion": "apps/v1",
  "kind": "Deployment",
  "metadata": {
    "name": "my-app",
    "namespace": "production",
    "labels": { "app": "my-app" },
    "annotations": { "deployment.kubernetes.io/revision": "3" },
    "creationTimestamp": "2024-01-01T00:00:00Z"
  },
  "status": {
    "replicas": 3,
    "readyReplicas": 3,
    "updatedReplicas": 3,
    "availableReplicas": 3,
    "conditions": [
      {
        "type": "Available",
        "status": "True",
        "lastTransitionTime": "2024-01-01T00:05:00Z",
        "reason": "MinimumReplicasAvailable",
        "message": "Deployment has minimum availability."
      }
    ]
  }
}
```

### Auth Methods

| Product | Auth Methods |
|---|---|
| **Argo CD** | Bearer JWT token (via `/api/v1/session`), SSO (OIDC, OAuth2, SAML) |
| **Kubernetes** | Bearer token (ServiceAccount), client certificate (mTLS), OIDC |
| **Flux CD** | Inherits Kubernetes auth (RBAC) |

### .NET Client Libraries

| Library | NuGet | Notes |
|---|---|---|
| `KubernetesClient` | `kubernetes-client/csharp` | Official K8s client; typed models, watch support |
| Argo CD | `HttpClient` + JSON | Generate from Swagger spec, or use gRPC |
| Flux | `KubernetesClient` | Access CRDs via generic `CustomObjectsApi` |

---

## 6. Git / SCM

### Products

| Product | Notes |
|---|---|
| **GitHub** | REST v3 + GraphQL v4; predominant public SCM |
| **GitLab** | REST v4; self-hosted or SaaS |
| **Azure DevOps** | REST; Azure Repos, Pipelines |
| **Bitbucket** | REST v2 (Cloud) / v1 (Server) |

### Query API Model

#### GitHub REST API

| Endpoint | Method | Purpose |
|---|---|---|
| `/repos/{owner}/{repo}/commits` | GET | List commits |
| `/repos/{owner}/{repo}/commits/{ref}` | GET | Get commit detail |
| `/repos/{owner}/{repo}/compare/{basehead}` | GET | Compare two refs |
| `/repos/{owner}/{repo}/pulls` | GET | List pull requests |
| `/repos/{owner}/{repo}/pulls/{number}` | GET | Get PR detail |
| `/repos/{owner}/{repo}/actions/runs` | GET | List workflow runs |
| `/repos/{owner}/{repo}/deployments` | GET | List deployments |
| `/repos/{owner}/{repo}/branches` | GET | List branches |
| `/repos/{owner}/{repo}/tags` | GET | List tags |

**GET /repos/{owner}/{repo}/commits parameters:**

| Param | Type | Description |
|---|---|---|
| `sha` | string | SHA or branch to start listing from |
| `path` | string | Only commits containing this file path |
| `author` | string | GitHub login or email |
| `committer` | string | GitHub login or email |
| `since` | ISO 8601 | Only commits after this date |
| `until` | ISO 8601 | Only commits before this date |
| `per_page` | int | Results per page (max 100) |
| `page` | int | Page number |

#### GitLab REST API v4

| Endpoint | Method | Purpose |
|---|---|---|
| `/projects/:id/repository/commits` | GET | List commits |
| `/projects/:id/repository/commits/:sha` | GET | Get commit |
| `/projects/:id/repository/commits/:sha/diff` | GET | Commit diff |
| `/projects/:id/repository/commits/:sha/merge_requests` | GET | MRs for commit |
| `/projects/:id/repository/commits/:sha/statuses` | GET | CI statuses |
| `/projects/:id/merge_requests` | GET | List merge requests |
| `/projects/:id/pipelines` | GET | List CI/CD pipelines |
| `/projects/:id/deployments` | GET | List deployments |
| `/projects/:id/repository/branches` | GET | List branches |
| `/projects/:id/repository/tags` | GET | List tags |

**GET /projects/:id/repository/commits parameters:**

| Param | Type | Description |
|---|---|---|
| `ref_name` | string | Branch or tag name |
| `since` | ISO 8601 | Commits after this date |
| `until` | ISO 8601 | Commits before this date |
| `path` | string | File path filter |
| `author` | string | Author email or name |
| `all` | bool | Retrieve commits from all branches |
| `with_stats` | bool | Include addition/deletion stats |
| `per_page` | int | Results per page (max 100) |
| `page` | int | Page number |

#### Azure DevOps REST API

| Endpoint | Method | Purpose |
|---|---|---|
| `/{org}/{project}/_apis/git/repositories/{repo}/commits` | GET | List commits |
| `/{org}/{project}/_apis/git/repositories/{repo}/pullrequests` | GET | List PRs |
| `/{org}/{project}/_apis/build/builds` | GET | List builds |
| `/{org}/{project}/_apis/release/releases` | GET | List releases |

### Response Shape

#### GitHub — Commit object

```json
{
  "sha": "abc123def456",
  "node_id": "C_kw...",
  "commit": {
    "author": { "name": "John", "email": "john@example.com", "date": "2024-01-01T12:00:00Z" },
    "committer": { "name": "John", "email": "john@example.com", "date": "2024-01-01T12:00:00Z" },
    "message": "fix: resolve connection timeout issue",
    "tree": { "sha": "tree123", "url": "..." },
    "verification": { "verified": true, "reason": "valid" }
  },
  "author": { "login": "johndoe", "id": 12345, "avatar_url": "..." },
  "committer": { "login": "johndoe", "id": 12345 },
  "stats": { "additions": 12, "deletions": 5, "total": 17 },
  "files": [
    {
      "sha": "file123",
      "filename": "src/connection.ts",
      "status": "modified",
      "additions": 8,
      "deletions": 3,
      "changes": 11,
      "patch": "@@ -10,3 +10,8 @@ ..."
    }
  ]
}
```

#### GitLab — Commit object

```json
{
  "id": "abc123def456789",
  "short_id": "abc123d",
  "title": "fix: resolve connection timeout issue",
  "message": "fix: resolve connection timeout issue\n\nDetailed description...",
  "author_name": "John Doe",
  "author_email": "john@example.com",
  "authored_date": "2024-01-01T12:00:00.000+00:00",
  "committer_name": "John Doe",
  "committer_email": "john@example.com",
  "committed_date": "2024-01-01T12:00:00.000+00:00",
  "created_at": "2024-01-01T12:00:00.000+00:00",
  "parent_ids": ["parent789"],
  "web_url": "https://gitlab.com/org/repo/-/commit/abc123def456789",
  "stats": { "additions": 12, "deletions": 5, "total": 17 },
  "status": "success",
  "last_pipeline": {
    "id": 456,
    "ref": "main",
    "sha": "abc123def456789",
    "status": "success",
    "web_url": "https://gitlab.com/org/repo/-/pipelines/456"
  }
}
```

### Auth Methods

| Product | Auth Methods |
|---|---|
| **GitHub** | Personal Access Token (`Authorization: Bearer <token>`), GitHub App (JWT + installation token), OAuth App |
| **GitLab** | Personal Access Token (`PRIVATE-TOKEN: <token>`), OAuth 2.0, Project/Group Access Token, Deploy Token |
| **Azure DevOps** | Personal Access Token (Basic Auth: `:PAT` base64), OAuth 2.0, Azure AD (managed identity) |

### .NET Client Libraries

| Library | NuGet | Notes |
|---|---|---|
| `Octokit` | `Octokit` | Official GitHub .NET client; strongly typed |
| `Octokit.GraphQL` | `Octokit.GraphQL` | GitHub GraphQL client |
| `NGitLab` | `NGitLab` | Community GitLab client |
| `GitLabApiClient` | `GitLabApiClient` | Alternative GitLab client |
| `Microsoft.TeamFoundation.SourceControl` | Azure DevOps SDK | Official Azure DevOps client |
| `Microsoft.VisualStudio.Services.Client` | Azure DevOps SDK | Connection/auth layer |

---

## Cross-Category Comparison Table

| Dimension | Metrics | Logs | Tracing | Alerting | Deployment | Git/SCM |
|---|---|---|---|---|---|---|
| **Query Language** | PromQL | LogQL / Lucene DSL | TraceQL / tag filters | Label matchers | Label selectors | Path/author/date filters |
| **Primary Endpoint** | `/api/v1/query_range` | `/loki/api/v1/query_range` or `/_search` | `/api/traces/{id}` or `/api/search` | `/api/v2/alerts` | `/api/v1/applications` | `/repos/{o}/{r}/commits` |
| **Time Range Params** | `start`, `end`, `step` | `start`, `end`, `step`, `since` | `start`, `end` | N/A (alerts are live state) | N/A (current state) | `since`, `until` |
| **Time Format** | RFC3339 or unix seconds | RFC3339 or unix nanoseconds | Unix microseconds (Jaeger) / seconds (Tempo) | RFC3339 | RFC3339 (K8s) | ISO 8601 |
| **Pagination** | `limit` param | `limit` + `direction` | `limit` | `filter` param | `limit` + `continue` token | `per_page` + `page` |
| **Filtering** | PromQL label matchers `{job="x"}` | LogQL stream selectors `{app="x"}` | Tag key-value filters | Label `filter` param | K8s label/field selectors | Query params (author, path) |
| **Response Envelope** | `{status, data:{resultType, result}}` | `{status, data:{resultType, result, stats}}` | `{data:[{traceID, spans}]}` or OTLP | JSON array of alert objects | K8s object / Argo JSON | JSON array / object |
| **Value Shape** | `[timestamp, "value"]` tuple | `[nano_ts, "log_line"]` tuple | Span tree (parent-child refs) | Label + annotation dict | Resource tree | Commit + diff + stats |
| **Streaming** | No (poll) | WebSocket (`/tail`) | No (poll) | No (poll) | K8s `?watch=true` (SSE) | No (webhook push) |
| **Multi-tenancy** | External / Mimir tenant header | `X-Scope-OrgID` header | `X-Scope-OrgID` (Tempo) | Per-receiver routing | K8s namespaces / RBAC | Org/project/repo hierarchy |
| **Default Auth** | None | None / `X-Scope-OrgID` | None | None | Bearer JWT / ServiceAccount | PAT / OAuth |
| **Best .NET Client** | `HttpClient` | `Elastic.Clients.Elasticsearch` or `HttpClient` | `HttpClient` | `HttpClient` | `KubernetesClient` | `Octokit` / `NGitLab` |

---

## Key Design Insights for Unified Abstraction

### 1. Universal Query Primitives

Every data source query can be decomposed into 4 primitives:

| Primitive | Description | Examples |
|---|---|---|
| **TimeRange** | Start + End timestamps (normalize to `DateTimeOffset`) | `start/end`, `since/until` |
| **Filter** | Key-value label/tag matchers | PromQL `{job="x"}`, LogQL `{app="x"}`, K8s `labelSelector`, ES `term` query |
| **Query Expression** | Domain-specific query string (optional) | PromQL, LogQL, TraceQL, Lucene, KQL |
| **Pagination** | Limit + cursor/offset | `limit`, `per_page+page`, `limit+continue` |

**Suggested interface:**

```
IDataSourceQuery {
    TimeRange: { Start: DateTimeOffset, End: DateTimeOffset }
    Filters: Dictionary<string, string>
    Expression: string?            // PromQL, LogQL, TraceQL, etc.
    Limit: int
    Cursor: string?                // opaque pagination token
}
```

### 2. Response Shape as a Discriminated Union

All responses fall into ~5 archetype shapes:

| Archetype | Used By | Shape |
|---|---|---|
| **TimeSeries** | Metrics (range), Loki metric queries | `{ labels: {}, values: [(timestamp, value)] }[]` |
| **LogStream** | Loki log queries | `{ labels: {}, entries: [(timestamp, line)] }[]` |
| **SpanTree** | Jaeger, Tempo, Zipkin | `{ traceId, spans: [{ spanId, parentId, name, duration, tags }] }` |
| **AlertList** | Alertmanager, PagerDuty, Opsgenie | `{ alerts: [{ id, status, labels, annotations, timestamp }] }` |
| **ResourceState** | K8s, Argo CD, Git | `{ items: [{ id, name, status, metadata, children? }] }` |

**Suggested approach:** Use a `DataSourceResultType` enum with typed result containers.

### 3. Authentication Abstraction

Three dominant auth patterns emerge:

| Pattern | Products | Implementation |
|---|---|---|
| **Bearer Token** | GitHub, GitLab, ArgoCD, K8s, PagerDuty | `Authorization: Bearer <token>` |
| **API Key / Custom Header** | Opsgenie, Loki (`X-Scope-OrgID`), PagerDuty | Custom header name + value |
| **Basic Auth** | Elasticsearch, Grafana Cloud, Azure DevOps | `Authorization: Basic <base64>` |

**Suggested interface:**

```
IDataSourceAuth {
    AuthType: Bearer | ApiKey | Basic | None
    Credentials: { Header: string, Value: string }
    // For K8s: could also carry kubeconfig/certificate
}
```

### 4. Timestamp Normalization is Critical

| Product | Time Format |
|---|---|
| Prometheus | Unix seconds (float) |
| Loki | Unix nanoseconds (string) |
| Jaeger | Unix microseconds (int64) |
| Tempo | Unix seconds (int) or nanoseconds (string) |
| Elasticsearch | ISO 8601 / epoch millis |
| GitHub/GitLab | ISO 8601 |
| Kubernetes | RFC3339 |
| Alertmanager | RFC3339 |

**All must normalize to `DateTimeOffset` on the .NET side.** Build a `TimestampConverter` that handles all variants.

### 5. Connection Configuration Model

```
DataSourceConnection {
    Id: Guid
    Name: string
    Category: Metrics | Logs | Tracing | Alerting | Deployment | Git
    Product: Prometheus | Loki | Elasticsearch | Jaeger | Tempo | ...
    BaseUrl: Uri
    Auth: IDataSourceAuth
    CustomHeaders: Dictionary<string, string>   // e.g. X-Scope-OrgID
    TlsConfig: { SkipVerify: bool, CaCert: string? }
    Timeout: TimeSpan
}
```

### 6. Product-Specific Query Translators

The unified abstraction should have a **strategy pattern**:

```
IQueryTranslator {
    BuildHttpRequest(query: IDataSourceQuery) -> HttpRequestMessage
    ParseResponse<T>(response: HttpResponseMessage) -> DataSourceResult<T>
}
```

Each product gets its own translator implementation:
- `PrometheusQueryTranslator` — builds `/api/v1/query_range?query=...&start=...&end=...`
- `LokiQueryTranslator` — builds `/loki/api/v1/query_range?query=...`
- `ElasticsearchQueryTranslator` — builds `POST /_search` with Query DSL body
- `JaegerQueryTranslator` — builds `/api/traces/{id}` or `/api/traces?service=...`
- etc.

### 7. Metadata / Discovery APIs

Every category has introspection endpoints:

| Category | Discovery Endpoints |
|---|---|
| Metrics | `/api/v1/labels`, `/api/v1/label/{name}/values`, `/api/v1/metadata` |
| Logs (Loki) | `/loki/api/v1/labels`, `/loki/api/v1/label/{name}/values` |
| Logs (ES) | `/_cat/indices`, `/{index}/_mapping` |
| Tracing | `/api/services`, `/api/services/{svc}/operations`, `/api/search/tags` |
| Alerting | `/api/v2/receivers`, `/api/v2/status` |
| Deployment | `/api/v1/clusters`, `/api/v1/projects` |
| Git | `/repos/{o}/{r}/branches`, `/repos/{o}/{r}/tags` |

These should be surfaced through a `IDataSourceMetadataProvider` interface for building dynamic UIs (autocomplete, label pickers, etc.).

### 8. Health Check Pattern

All products support some form of health/status check:

| Product | Health Endpoint |
|---|---|
| Prometheus | `GET /-/healthy` + `GET /api/v1/status/buildinfo` |
| Loki | `GET /ready` |
| Elasticsearch | `GET /` (cluster info) + `GET /_cluster/health` |
| Jaeger | gRPC health check |
| Tempo | `GET /ready` |
| Alertmanager | `GET /api/v2/status` |
| Argo CD | `GET /api/v1/session` (validates auth) |
| Kubernetes | `GET /healthz` |
| GitHub | `GET /rate_limit` |
| GitLab | `GET /version` |

Build a `IDataSourceHealthChecker` that validates connectivity + auth before saving a connection.
