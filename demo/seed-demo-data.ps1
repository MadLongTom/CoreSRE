<#
.SYNOPSIS
    CoreSRE Demo 数据预注册脚本 — 一键初始化演示所需的 Agent / SOP / Tool 绑定 / 告警规则
.DESCRIPTION
    基于系统中已录入的 "v3-api" Provider，创建:
    1. 2 个 ChatClient Agent (ops-agent, log-analyst)
    2. 1 个 Team Agent (incident-response-team)
    3. 1 个自定义 SOP (high-error-rate-response) 含业务背景 reference files
    4. SOP 业务背景参考文件上传
    5. 3 条告警规则 (HighErrorRate / HighLatency / ServiceDown)
.PARAMETER ApiBase
    CoreSRE 后端 API 基础地址 (default: http://localhost:5156)
.PARAMETER Force
    跳过确认提示，强制执行
#>
[CmdletBinding()]
param(
    [string]$ApiBase = "http://localhost:5156",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
$sopFilesDir = Join-Path $scriptRoot "sop-files"

# ──────────── Colors ────────────
function Write-Step($msg)  { Write-Host "`n▶ $msg" -ForegroundColor Cyan }
function Write-Ok($msg)    { Write-Host "  ✓ $msg" -ForegroundColor Green }
function Write-Warn($msg)  { Write-Host "  ⚠ $msg" -ForegroundColor Yellow }
function Write-Err($msg)   { Write-Host "  ✗ $msg" -ForegroundColor Red }
function Write-Info($msg)  { Write-Host "  ℹ $msg" -ForegroundColor Gray }

# ──────────── HTTP Helper ────────────
function Invoke-Api {
    param(
        [string]$Method = "GET",
        [string]$Path,
        [object]$Body = $null,
        [int]$TimeoutSec = 15
    )
    $uri = "$ApiBase$Path"
    $params = @{
        Uri            = $uri
        Method         = $Method
        ContentType    = "application/json; charset=utf-8"
        TimeoutSec     = $TimeoutSec
    }
    if ($Body) {
        $json = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Depth 10 }
        $params["Body"] = [System.Text.Encoding]::UTF8.GetBytes($json)
    }
    try {
        $resp = Invoke-RestMethod @params
        return $resp
    } catch {
        $code = $_.Exception.Response.StatusCode.value__
        if ($code -eq 409) {
            return @{ success = $false; conflict = $true; message = "Already exists (409)" }
        }
        throw
    }
}

# ──────────── Pre-flight Check ────────────
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White
Write-Host "  CoreSRE Demo Data Seeder" -ForegroundColor White
Write-Host "  API: $ApiBase" -ForegroundColor Gray
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White

Write-Step "Checking API connectivity ..."
try {
    $null = Invoke-RestMethod -Uri "$ApiBase/api/agents" -TimeoutSec 5
    Write-Ok "CoreSRE API is reachable."
} catch {
    Write-Err "Cannot reach CoreSRE API at $ApiBase. Run '.\dev.ps1' first."
    exit 1
}

# ──────────── 1. Resolve v3-api Provider ────────────
Write-Step "Resolving 'v3-api' LLM Provider ..."
$providersResp = Invoke-Api -Path "/api/providers"
$provider = ($providersResp.data | Where-Object { $_.name -eq "v3-api" }) | Select-Object -First 1

if (-not $provider) {
    Write-Err "Provider 'v3-api' not found. Please register it manually in the UI first."
    Write-Host "  需要在 Providers 页面注册名为 'v3-api' 的 LLM Provider" -ForegroundColor Yellow
    exit 1
}

$providerId = $provider.id
Write-Ok "Found provider 'v3-api' (id=$providerId)"

# 获取模型列表，取第一个可用模型
$modelId = $provider.modelId
if (-not $modelId) {
    # 如果 provider 本身没有 modelId，尝试从 models 列表获取
    $modelId = ($provider.models | Select-Object -First 1)?.id
}
if (-not $modelId) {
    # 兜底: 使用常见模型名
    $modelId = "gpt-4o"
    Write-Warn "Could not auto-detect model ID, defaulting to '$modelId'"
}
Write-Ok "Using model: $modelId"

# ──────────── 2. Get existing DataSources ────────────
Write-Step "Checking registered data sources ..."
$dsResp = Invoke-Api -Path "/api/datasources"
$datasources = $dsResp.data

$dsPrometheus = ($datasources | Where-Object { $_.name -eq "k8s-prometheus" }) | Select-Object -First 1
$dsLoki = ($datasources | Where-Object { $_.name -eq "k8s-loki" }) | Select-Object -First 1
$dsJaeger = ($datasources | Where-Object { $_.name -eq "k8s-jaeger" }) | Select-Object -First 1
$dsAlertmanager = ($datasources | Where-Object { $_.name -eq "k8s-alertmanager" }) | Select-Object -First 1
$dsK8s = ($datasources | Where-Object { $_.name -eq "k8s-cluster" }) | Select-Object -First 1

if (-not $dsAlertmanager) {
    Write-Warn "Alertmanager DataSource not found. Run '.\deploy-demo.ps1' first to deploy K8s stack."
}

foreach ($ds in @(
    @{ name = "k8s-prometheus"; obj = $dsPrometheus },
    @{ name = "k8s-loki"; obj = $dsLoki },
    @{ name = "k8s-jaeger"; obj = $dsJaeger },
    @{ name = "k8s-alertmanager"; obj = $dsAlertmanager },
    @{ name = "k8s-cluster"; obj = $dsK8s }
)) {
    if ($ds.obj) { Write-Ok "$($ds.name) (id=$($ds.obj.id))" }
    else { Write-Warn "$($ds.name) — not found" }
}

# ──────────── 3. Get existing Tools (auto-generated from DataSources) ────────────
Write-Step "Checking available tools ..."
$toolsResp = Invoke-Api -Path "/api/tools?pageSize=100"
$tools = $toolsResp.data.items
if (-not $tools) { $tools = $toolsResp.data }

# Collect tool IDs for binding to agents
$toolRefs = @()
foreach ($t in $tools) {
    Write-Info "Tool: $($t.name) (id=$($t.id))"
    $toolRefs += $t.id
}
Write-Ok "Found $($toolRefs.Count) tools available"

# Build DataSourceRefs — bind all datasources, enable mutations on k8s-cluster
$dataSourceRefs = @()
foreach ($ds in @(
    @{ obj = $dsPrometheus; mutations = $false },
    @{ obj = $dsLoki; mutations = $false },
    @{ obj = $dsJaeger; mutations = $false },
    @{ obj = $dsAlertmanager; mutations = $false },
    @{ obj = $dsK8s; mutations = $true }
)) {
    if ($ds.obj) {
        $dataSourceRefs += @{
            dataSourceId    = $ds.obj.id
            enableMutations = $ds.mutations
        }
    }
}
Write-Ok "Built $($dataSourceRefs.Count) dataSourceRefs (k8s-cluster mutations enabled)"

# ──────────── 4. Register ChatClient Agents ────────────
Write-Step "Registering ChatClient agents ..."

# Agent 1: ops-agent (SRE 运维专家)
$opsAgentBody = @{
    name        = "ops-agent"
    description = "SRE 运维专家 — 负责故障诊断、指标分析、日志查询、链路追踪，是事件响应的主力 Agent"
    agentType   = "ChatClient"
    llmConfig   = @{
        modelId         = $modelId
        providerId      = $providerId
        instructions    = @"
你是一个资深的 SRE (Site Reliability Engineering) 运维专家。

## 职责
- 监控分布式微服务系统的健康状态
- 分析 Prometheus 指标、Loki 日志、Jaeger 链路追踪数据
- 诊断故障根因、评估影响范围
- 制定和执行修复方案

## 当前管理的系统
你管理的是 demo-app 命名空间下的电商微服务系统，包含:
- order-service (订单服务，2 副本) — 接收订单，编排调用 payment + inventory
- payment-service (支付服务，2 副本) — 处理支付，有 3% 内建随机故障率
- inventory-service (库存服务，2 副本) — 管理库存，有 2% 内建随机缺货率

## 工作原则
1. 先用数据说话：查询指标确认问题存在和范围
2. 多维度交叉验证：Metrics + Logs + Traces 三位一体
3. 给出明确结论：根因、影响范围、建议修复方案
4. 区分系统内建故障率和真实异常

## 可用的 Skills
当收到告警事件时，使用 read_skill 工具加载对应的 SOP 进行标准化处置。
SOP 中标记了 📦 的技能有业务背景参考文件，使用 read_skill_file 工具加载参考文件。
"@
        toolRefs        = $toolRefs
        dataSourceRefs  = $dataSourceRefs
        temperature     = 0.3
        maxOutputTokens = 4096
        enableChatHistory = $true
    }
}

$opsAgentResp = Invoke-Api -Method POST -Path "/api/agents" -Body $opsAgentBody
if ($opsAgentResp.success -or $opsAgentResp.data) {
    $opsAgentId = $opsAgentResp.data.id
    Write-Ok "ops-agent (id=$opsAgentId)"
} elseif ($opsAgentResp.conflict) {
    Write-Warn "ops-agent already exists, looking up by name ..."
    $existingAgents = (Invoke-Api -Path "/api/agents").data
    $opsAgentId = ($existingAgents | Where-Object { $_.name -eq "ops-agent" }).id
    Write-Ok "ops-agent (existing, id=$opsAgentId)"
} else {
    Write-Err "Failed to register ops-agent"
    Write-Host ($opsAgentResp | ConvertTo-Json -Depth 5) -ForegroundColor Red
}

# Agent 2: log-analyst (日志分析专家)
$logAnalystBody = @{
    name        = "log-analyst"
    description = "日志分析专家 — 擅长从海量日志中提取关键信息，识别错误模式和异常趋势"
    agentType   = "ChatClient"
    llmConfig   = @{
        modelId         = $modelId
        providerId      = $providerId
        instructions    = @"
你是一个日志分析专家，专注于从海量结构化日志中提取关键信息。

## 职责
- 分析 Loki 中的结构化 JSON 日志
- 识别错误模式、异常趋势
- 关联 trace_id 追踪跨服务调用
- 统计错误频率和分布

## 当前系统
分析的日志来自 demo-app 命名空间的微服务，日志格式:
{"timestamp": "...", "level": "info|error|warning", "msg": "...", "service": "...", "trace_id": "...", "span_id": "..."}

## 常见日志模式
- "Payment gateway timeout" — payment-service 支付网关超时
- "Insufficient stock for SKU-xxx" — inventory-service 库存不足
- "Order creation failed" — order-service 订单创建失败（通常由下游故障引起）

## 工作原则
1. 关注 error 和 warning 级别日志
2. 按时间线梳理事件序列
3. 利用 trace_id 关联多个服务的日志
4. 给出错误频率统计和趋势判断
"@
        toolRefs        = $toolRefs
        temperature     = 0.2
        maxOutputTokens = 4096
        enableChatHistory = $true
    }
}

$logAnalystResp = Invoke-Api -Method POST -Path "/api/agents" -Body $logAnalystBody
if ($logAnalystResp.success -or $logAnalystResp.data) {
    $logAnalystId = $logAnalystResp.data.id
    Write-Ok "log-analyst (id=$logAnalystId)"
} elseif ($logAnalystResp.conflict) {
    Write-Warn "log-analyst already exists, looking up ..."
    $existingAgents = (Invoke-Api -Path "/api/agents").data
    $logAnalystId = ($existingAgents | Where-Object { $_.name -eq "log-analyst" }).id
    Write-Ok "log-analyst (existing, id=$logAnalystId)"
} else {
    Write-Err "Failed to register log-analyst"
}

# ──────────── 5. Register Team Agent ────────────
Write-Step "Registering Team agent ..."

if ($opsAgentId -and $logAnalystId) {
    $teamBody = @{
        name        = "incident-response-team"
        description = "事件响应团队 — 由 SRE 运维专家和日志分析专家组成，Selector 模式下 LLM 根据对话上下文自动选择最合适的 Agent 应答"
        agentType   = "Team"
        teamConfig  = @{
            mode              = "Selector"
            participantIds    = @($opsAgentId, $logAnalystId)
            maxIterations     = 20
            selectorProviderId = $providerId
            selectorModelId   = $modelId
        }
    }

    $teamResp = Invoke-Api -Method POST -Path "/api/agents" -Body $teamBody
    if ($teamResp.success -or $teamResp.data) {
        $teamAgentId = $teamResp.data.id
        Write-Ok "incident-response-team (id=$teamAgentId)"
    } elseif ($teamResp.conflict) {
        Write-Warn "incident-response-team already exists, looking up ..."
        $existingAgents = (Invoke-Api -Path "/api/agents").data
        $teamAgentId = ($existingAgents | Where-Object { $_.name -eq "incident-response-team" }).id
        Write-Ok "incident-response-team (existing, id=$teamAgentId)"
    } else {
        Write-Err "Failed to register Team agent"
    }
} else {
    Write-Warn "Skipping Team agent — need both ops-agent and log-analyst."
}

# ──────────── 6. Register SOP (Skill) ────────────
Write-Step "Registering SOP: high-error-rate-response ..."

$sopContent = @'
# SOP: HTTP 高错误率应急响应

## 适用条件

- 告警名称: HighErrorRate
- 触发条件: HTTP 5xx 错误率超过 5% 持续 2 分钟
- 适用服务: demo-app 命名空间下的所有微服务 (order-service, payment-service, inventory-service)
- 严重级别: P2

## 初始化上下文

- Metrics: sum(rate(http_requests_total{namespace="${namespace}", status=~"5.."}[5m])) by (app) | 各服务错误率 | lookback=30m
- Metrics: sum(rate(http_requests_total{namespace="${namespace}"}[5m])) by (app) | 各服务 QPS | lookback=30m
- Metrics: histogram_quantile(0.99, sum(rate(http_request_duration_seconds_bucket{namespace="${namespace}"}[5m])) by (le, app)) | P99 延迟 | lookback=30m
- Logs: {namespace="${namespace}"} |= "error" | 错误日志 | lookback=15m
- Deployment: pods/${namespace} | Pod 状态

## 处置步骤

### Step 1: 确认告警指标

查询 Prometheus 指标，确认各服务的 HTTP 5xx 错误率:

```promql
sum(rate(http_requests_total{namespace="demo-app", status=~"5.."}[5m])) by (app)
/
sum(rate(http_requests_total{namespace="demo-app"}[5m])) by (app)
```

**预期结果**: 确认哪些服务的 5xx 率异常（正常 baseline: order ~5%, payment ~3%, inventory ~2%）
**超时**: 30 秒

### Step 2: 定位错误源头

对错误率异常的服务，检查日志中的具体错误信息:

```logql
{namespace="demo-app", app="<异常服务名>"} |= "error"
```

常见错误模式:
- "Payment gateway timeout" → payment-service 支付网关超时（内建 3% 故障率）
- "Insufficient stock for SKU-xxx" → inventory-service 库存不足（内建 2% 故障率）
- "Order creation failed: ..." → order-service 因下游故障失败

**预期结果**: 识别出错误是内建随机故障还是系统性异常
**超时**: 30 秒

### Step 3: 链路追踪分析

如果错误率显著高于正常 baseline，查询 Jaeger 中的错误链路:

在 Jaeger UI 中按 service 筛选，查看 error=true 的 trace，分析:
- 错误发生在哪个 span（哪个服务调用）
- 延迟分布是否异常
- 是否有 timeout 模式

**预期结果**: 确认根因是单服务故障还是级联故障
**超时**: 60 秒

### Step 4: 检查 Pod 健康状态

查询 Kubernetes Pod 状态:
- 是否有 Pod 处于 CrashLoopBackOff / Error / Pending
- 是否有 OOMKill 事件
- 副本数是否正常（期望每个服务 2 副本）

**预期结果**: 所有 Pod 应为 Running/Ready 状态
**超时**: 30 秒

### Step 5: 评估与建议

根据以上分析给出结论:

1. **如果错误率在正常范围内 (order <8%, payment <5%, inventory <3%)**:
   - 结论: 属于系统内建随机故障率的正常波动
   - 建议: 无需介入，持续观察

2. **如果错误率显著超标**:
   - 结论: 系统存在异常
   - 建议: 根据根因分类处理
     - 单 Pod 异常 → 重启 Pod: `kubectl delete pod <pod-name> -n demo-app`
     - Deployment 异常 → 重启部署: `kubectl rollout restart deployment/<service> -n demo-app`
     - 全局异常 → 升级为 P1 事件，触发 Team Agent 深度分析

**预期结果**: 给出明确的结论和下一步行动
**超时**: 30 秒

## 回退计划

如果修复操作导致服务状态恶化:
1. 立即回退: `kubectl rollout undo deployment/<service> -n demo-app`
2. 确认回退后服务恢复: 检查 `up{app="<service>"}` 指标
3. 记录事件并升级给人工处理

## 参考资料

本 SOP 的文件包中包含以下参考文件（使用 `read_skill_file` 工具查看）:
- `architecture.md` — 系统架构图和各服务详情
- `observability-queries.md` — 可观测性查询手册（PromQL / LogQL / Jaeger）
- `troubleshooting-guide.md` — 常见故障模式与修复指南
'@

$sopBody = @{
    name        = "high-error-rate-response"
    description = "HTTP 高错误率应急响应 SOP — 适用于 demo-app 微服务系统 5xx 错误率超阈值场景，包含指标确认、日志分析、链路追踪、Pod 检查的标准化处置流程"
    category    = "sop"
    content     = $sopContent
    scope       = "User"
    allowedTools = $toolRefs
}

$sopResp = Invoke-Api -Method POST -Path "/api/skills" -Body $sopBody
if ($sopResp.success -or $sopResp.data) {
    $sopId = $sopResp.data.id
    Write-Ok "SOP 'high-error-rate-response' (id=$sopId)"
} elseif ($sopResp.conflict) {
    Write-Warn "SOP already exists, looking up ..."
    $skillsResp = Invoke-Api -Path "/api/skills?pageSize=100"
    $items = $skillsResp.data.items
    if (-not $items) { $items = $skillsResp.data }
    $sopId = ($items | Where-Object { $_.name -eq "high-error-rate-response" }).id
    Write-Ok "SOP 'high-error-rate-response' (existing, id=$sopId)"
} else {
    Write-Err "Failed to register SOP"
    Write-Host ($sopResp | ConvertTo-Json -Depth 5) -ForegroundColor Red
}

# ──────────── 7. Upload SOP Reference Files ────────────
if ($sopId) {
    Write-Step "Uploading SOP reference files ..."

    $refFiles = @("architecture.md", "observability-queries.md", "troubleshooting-guide.md")

    foreach ($fileName in $refFiles) {
        $filePath = Join-Path $sopFilesDir $fileName
        if (-not (Test-Path $filePath)) {
            Write-Warn "File not found: $filePath"
            continue
        }

        try {
            # Multipart form upload
            $fileBytes = [System.IO.File]::ReadAllBytes($filePath)
            $boundary = [System.Guid]::NewGuid().ToString()
            $LF = "`r`n"

            $bodyLines = @(
                "--$boundary",
                "Content-Disposition: form-data; name=`"files`"; filename=`"$fileName`"",
                "Content-Type: text/markdown",
                "",
                [System.Text.Encoding]::UTF8.GetString($fileBytes),
                "--$boundary--"
            )
            $bodyStr = $bodyLines -join $LF

            $resp = Invoke-RestMethod `
                -Uri "$ApiBase/api/skills/$sopId/files?prefix=reference" `
                -Method Post `
                -ContentType "multipart/form-data; boundary=$boundary" `
                -Body ([System.Text.Encoding]::UTF8.GetBytes($bodyStr)) `
                -TimeoutSec 30

            Write-Ok "Uploaded reference/$fileName"
        } catch {
            Write-Err "Failed to upload $fileName`: $($_.Exception.Message)"
        }
    }
}

# ──────────── 8. Bind SOP (skillRef) to ops-agent ────────────
# NOTE: 通过 RegisterSkill API 创建的 Skill 默认状态为 Active（可直接使用）。
#       Validate → Approve → Publish 生命周期仅适用于通过事件反馈循环自动生成的 SOP（Draft 状态）。
if ($sopId -and $opsAgentId) {
    Write-Step "Binding SOP to ops-agent ..."

    # 获取当前 agent 详情
    $agentDetail = Invoke-Api -Path "/api/agents/$opsAgentId"
    $currentAgent = $agentDetail.data

    if ($currentAgent.llmConfig) {
        $currentSkillRefs = @()
        if ($currentAgent.llmConfig.skillRefs) {
            $currentSkillRefs = @($currentAgent.llmConfig.skillRefs)
        }

        if ($currentSkillRefs -notcontains $sopId) {
            $currentSkillRefs += $sopId

            $updateBody = @{
                name        = $currentAgent.name
                description = $currentAgent.description
                agentType   = $currentAgent.agentType
                llmConfig   = @{
                    modelId           = $currentAgent.llmConfig.modelId
                    providerId        = $currentAgent.llmConfig.providerId
                    instructions      = $currentAgent.llmConfig.instructions
                    toolRefs          = @($currentAgent.llmConfig.toolRefs | ForEach-Object { $_ })
                    skillRefs         = $currentSkillRefs
                    temperature       = $currentAgent.llmConfig.temperature
                    maxOutputTokens   = $currentAgent.llmConfig.maxOutputTokens
                    enableChatHistory = $currentAgent.llmConfig.enableChatHistory
                }
            }

            try {
                $updateResp = Invoke-Api -Method PUT -Path "/api/agents/$opsAgentId" -Body $updateBody
                if ($updateResp.success) {
                    Write-Ok "SOP bound to ops-agent"
                } else {
                    Write-Warn "Binding returned: $($updateResp | ConvertTo-Json -Compress)"
                }
            } catch {
                Write-Warn "Failed to bind SOP: $($_.Exception.Message)"
            }
        } else {
            Write-Ok "SOP already bound to ops-agent"
        }
    }
}

# ──────────── 9. Register Alert Rules ────────────
Write-Step "Registering alert rules ..."

if (-not $opsAgentId) {
    Write-Warn "ops-agent not found — skipping alert rule registration."
} else {
    # Helper: find existing rule by name (returns first match or $null)
    $existingRulesResp = Invoke-Api -Path "/api/alert-rules"
    $existingRules = $existingRulesResp.data
    function Find-ExistingRule($ruleName) {
        ($existingRules | Where-Object { $_.name -eq $ruleName }) | Select-Object -First 1
    }
    # Create-or-update: if rule exists, PUT to update (preserves incident references); else POST
    function Upsert-AlertRule($ruleName, $body) {
        $existing = Find-ExistingRule $ruleName
        if ($existing) {
            $r = Invoke-Api -Method PUT -Path "/api/alert-rules/$($existing.id)" -Body $body
            if ($r.success -or $r.data) { Write-Ok "$ruleName (updated, id=$($existing.id))" }
        } else {
            $r = Invoke-Api -Method POST -Path "/api/alert-rules" -Body $body
            if ($r.success -or $r.data) { Write-Ok "$ruleName (created, id=$($r.data.id))" }
        }
    }

    # Rule 1: HighErrorRate → SOP 自动执行
    if ($sopId) {
        $rule1 = @{
            name               = "HighErrorRate"
            description        = "HTTP 5xx 错误率超过 5% 持续 2 分钟 → 自动执行 high-error-rate-response SOP"
            severity           = "P2"
            matchers           = @( @{ label = "alertname"; operator = "Eq"; value = "HighErrorRate" } )
            sopId              = $sopId
            responderAgentId   = $opsAgentId
            cooldownMinutes    = 15
            contextProviders   = @(
                @{ category = "Metrics"; expression = 'sum(rate(http_requests_total{namespace="${namespace}", status=~"5.."}[5m])) by (app)'; label = "各服务 5xx 错误率"; lookback = "30m" },
                @{ category = "Metrics"; expression = 'sum(rate(http_requests_total{namespace="${namespace}"}[5m])) by (app)'; label = "各服务 QPS"; lookback = "30m" },
                @{ category = "Metrics"; expression = 'histogram_quantile(0.99, sum(rate(http_request_duration_seconds_bucket{namespace="${namespace}"}[5m])) by (le, app))'; label = "P99 延迟"; lookback = "30m" },
                @{ category = "Logs"; expression = '{namespace="${namespace}"} |= "error"'; label = "错误日志"; lookback = "15m" },
                @{ category = "Deployment"; expression = 'pods/${namespace}'; label = "Pod 状态" }
            )
        }
        try {
            Upsert-AlertRule "HighErrorRate" $rule1 | Out-Null
        } catch { Write-Err "HighErrorRate — $($_.Exception.Message)" }
    }

    # Rule 2: HighLatency → SOP 自动执行 (复用同一 SOP)
    if ($sopId) {
        $rule2 = @{
            name               = "HighLatency"
            description        = "P99 延迟超过 1s 持续 5 分钟 → 自动执行应急响应 SOP"
            severity           = "P3"
            matchers           = @( @{ label = "alertname"; operator = "Eq"; value = "HighLatency" } )
            sopId              = $sopId
            responderAgentId   = $opsAgentId
            cooldownMinutes    = 30
            contextProviders   = @(
                @{ category = "Metrics"; expression = 'histogram_quantile(0.99, sum(rate(http_request_duration_seconds_bucket{namespace="${namespace}"}[5m])) by (le, app))'; label = "P99 延迟趋势"; lookback = "30m" },
                @{ category = "Metrics"; expression = 'histogram_quantile(0.50, sum(rate(http_request_duration_seconds_bucket{namespace="${namespace}"}[5m])) by (le, app))'; label = "P50 延迟趋势"; lookback = "30m" },
                @{ category = "Metrics"; expression = 'sum(rate(http_requests_total{namespace="${namespace}"}[5m])) by (app)'; label = "各服务 QPS"; lookback = "30m" },
                @{ category = "Logs"; expression = '{namespace="${namespace}"} |~ "timeout|slow|latency"'; label = "延迟相关日志"; lookback = "15m" },
                @{ category = "Deployment"; expression = 'pods/${namespace}'; label = "Pod 状态" }
            )
        }
        try {
            Upsert-AlertRule "HighLatency" $rule2 | Out-Null
        } catch { Write-Err "HighLatency — $($_.Exception.Message)" }
    }

    # Rule 3: ServiceDown → Team Agent 根因分析
    if ($teamAgentId) {
        $rule3 = @{
            name               = "ServiceDown"
            description        = "服务宕机(up==0 持续 1 分钟) → Team Agent 深度根因分析"
            severity           = "P1"
            matchers           = @( @{ label = "alertname"; operator = "Eq"; value = "ServiceDown" } )
            teamAgentId        = $teamAgentId
            summarizerAgentId  = $opsAgentId
            cooldownMinutes    = 10
            contextProviders   = @(
                @{ category = "Metrics"; expression = 'up{namespace="${namespace}"}'; label = "服务存活状态"; lookback = "15m" },
                @{ category = "Metrics"; expression = 'sum(rate(http_requests_total{namespace="${namespace}"}[5m])) by (app)'; label = "各服务 QPS"; lookback = "30m" },
                @{ category = "Metrics"; expression = 'sum(rate(http_requests_total{namespace="${namespace}", status=~"5.."}[5m])) by (app)'; label = "5xx 错误率"; lookback = "30m" },
                @{ category = "Logs"; expression = '{namespace="${namespace}"} |~ "error|panic|fatal|OOMKilled|CrashLoopBackOff"'; label = "严重错误日志"; lookback = "30m" },
                @{ category = "Deployment"; expression = 'pods/${namespace}'; label = "Pod 状态" },
                @{ category = "Deployment"; expression = 'events/${namespace}'; label = "K8s 事件" }
            )
        }
        try {
            Upsert-AlertRule "ServiceDown" $rule3 | Out-Null
        } catch { Write-Err "ServiceDown — $($_.Exception.Message)" }
    } else {
        Write-Warn "Skipping ServiceDown rule — no Team agent available."
    }
}

# ──────────── Summary ────────────
Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White
Write-Host "  Demo Data Seeding Complete!" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White
Write-Host ""
Write-Host "  Created resources:" -ForegroundColor White
Write-Host "    Provider:   v3-api (id=$providerId)" -ForegroundColor Gray
Write-Host "    Agents:     ops-agent (id=$opsAgentId)" -ForegroundColor Gray
Write-Host "                log-analyst (id=$logAnalystId)" -ForegroundColor Gray
Write-Host "                incident-response-team (id=$teamAgentId)" -ForegroundColor Gray
Write-Host "    SOP:        high-error-rate-response (id=$sopId) [Active]" -ForegroundColor Gray
Write-Host "    Files:      3 reference files uploaded" -ForegroundColor Gray
Write-Host "    Rules:      HighErrorRate / HighLatency / ServiceDown" -ForegroundColor Gray
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor Yellow
Write-Host "    1. Open http://localhost:5173 to see agents registered" -ForegroundColor White
Write-Host "    2. Use '.\fire-alert.ps1 -Type HighErrorRate' to trigger incidents" -ForegroundColor White
Write-Host "    3. Watch the Incidents page for automated response" -ForegroundColor White
Write-Host ""
