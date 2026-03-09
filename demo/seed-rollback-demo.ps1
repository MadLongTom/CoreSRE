<#
.SYNOPSIS
    CoreSRE 回滚 Demo 数据注册 — 注册部署回滚 SOP 和告警规则
.DESCRIPTION
    在已有的 seed-demo-data.ps1 基础上，额外注册:
    1. deployment-rollback-response SOP (自动回滚 SOP)
    2. DeploymentErrorSpike 告警规则 (关联回滚 SOP)
    3. 更新 HighErrorRate 规则指向回滚 SOP (可选)

    前置条件:
    - CoreSRE 后端运行中
    - 已执行 seed-demo-data.ps1 (ops-agent 和工具已注册)
.PARAMETER ApiBase
    CoreSRE 后端 API 基础地址 (default: http://localhost:5156)
.PARAMETER OverrideHighErrorRate
    将现有的 HighErrorRate 规则也改为使用回滚 SOP
#>
[CmdletBinding()]
param(
    [string]$ApiBase = "http://localhost:5156",
    [switch]$OverrideHighErrorRate
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

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White
Write-Host "  CoreSRE Rollback Demo Data Seeder" -ForegroundColor White
Write-Host "  API: $ApiBase" -ForegroundColor Gray
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White

# ──────────── Pre-check: API + ops-agent ────────────
Write-Step "Checking prerequisites ..."
try {
    $null = Invoke-RestMethod -Uri "$ApiBase/api/agents" -TimeoutSec 5
    Write-Ok "CoreSRE API reachable"
} catch {
    Write-Err "Cannot reach CoreSRE API at $ApiBase. Run '.\dev.ps1' first."
    exit 1
}

# Find ops-agent
$agentsResp = Invoke-Api -Path "/api/agents"
$opsAgent = ($agentsResp.data | Where-Object { $_.name -eq "ops-agent" }) | Select-Object -First 1
if (-not $opsAgent) {
    Write-Err "ops-agent not found. Run '.\demo\seed-demo-data.ps1' first."
    exit 1
}
$opsAgentId = $opsAgent.id
Write-Ok "ops-agent found (id=$opsAgentId)"

# Get tools for SOP
$toolsResp = Invoke-Api -Path "/api/tools?pageSize=100"
$tools = $toolsResp.data.items
if (-not $tools) { $tools = $toolsResp.data }
$toolRefs = @($tools | ForEach-Object { $_.id })
Write-Ok "Found $($toolRefs.Count) tools"

# ──────────── 1. Register deployment-rollback-response SOP ────────────
Write-Step "Registering SOP: deployment-rollback-response ..."

$sopFilePath = Join-Path $sopFilesDir "deployment-rollback-response.md"
if (-not (Test-Path $sopFilePath)) {
    Write-Err "SOP file not found: $sopFilePath"
    exit 1
}

$sopContent = Get-Content $sopFilePath -Raw -Encoding UTF8

$sopBody = @{
    name        = "deployment-rollback-response"
    description = "部署回滚应急响应 SOP — 当检测到部署后错误率急剧上升时，自动分析指标并执行 Deployment 回滚"
    category    = "sop"
    content     = $sopContent
    scope       = "User"
    allowedTools = $toolRefs
}

$sopResp = Invoke-Api -Method POST -Path "/api/skills" -Body $sopBody
if ($sopResp.success -or $sopResp.data) {
    $rollbackSopId = $sopResp.data.id
    Write-Ok "SOP 'deployment-rollback-response' created (id=$rollbackSopId)"
} elseif ($sopResp.conflict) {
    Write-Warn "SOP already exists, looking up ..."
    $skillsResp = Invoke-Api -Path "/api/skills?pageSize=100"
    $items = $skillsResp.data.items
    if (-not $items) { $items = $skillsResp.data }
    $rollbackSopId = ($items | Where-Object { $_.name -eq "deployment-rollback-response" }).id
    Write-Ok "SOP 'deployment-rollback-response' found (existing, id=$rollbackSopId)"
} else {
    Write-Err "Failed to register SOP"
    Write-Host ($sopResp | ConvertTo-Json -Depth 5) -ForegroundColor Red
    exit 1
}

# ──────────── 2. Upload reference files to SOP ────────────
if ($rollbackSopId) {
    Write-Step "Uploading SOP reference files ..."

    $refFiles = @("architecture.md", "observability-queries.md", "troubleshooting-guide.md")

    foreach ($fileName in $refFiles) {
        $filePath = Join-Path $sopFilesDir $fileName
        if (-not (Test-Path $filePath)) {
            Write-Warn "File not found: $filePath"
            continue
        }

        try {
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
                -Uri "$ApiBase/api/skills/$rollbackSopId/files?prefix=reference" `
                -Method Post `
                -ContentType "multipart/form-data; boundary=$boundary" `
                -Body ([System.Text.Encoding]::UTF8.GetBytes($bodyStr)) `
                -TimeoutSec 30

            Write-Ok "Uploaded reference/$fileName"
        } catch {
            Write-Warn "Failed to upload $fileName`: $($_.Exception.Message)"
        }
    }
}

# ──────────── 3. Bind SOP to ops-agent ────────────
if ($rollbackSopId -and $opsAgentId) {
    Write-Step "Binding rollback SOP to ops-agent ..."

    $agentDetail = Invoke-Api -Path "/api/agents/$opsAgentId"
    $currentAgent = $agentDetail.data

    if ($currentAgent.llmConfig) {
        $currentSkillRefs = @()
        if ($currentAgent.llmConfig.skillRefs) {
            $currentSkillRefs = @($currentAgent.llmConfig.skillRefs)
        }

        if ($currentSkillRefs -notcontains $rollbackSopId) {
            $currentSkillRefs += $rollbackSopId

            $updateBody = @{
                name        = $currentAgent.name
                description = $currentAgent.description
                agentType   = $currentAgent.agentType
                llmConfig   = @{
                    modelId           = $currentAgent.llmConfig.modelId
                    providerId        = $currentAgent.llmConfig.providerId
                    instructions      = $currentAgent.llmConfig.instructions
                    toolRefs          = @($currentAgent.llmConfig.toolRefs | ForEach-Object { $_ })
                    dataSourceRefs    = @($currentAgent.llmConfig.dataSourceRefs | ForEach-Object { $_ })
                    skillRefs         = $currentSkillRefs
                    temperature       = $currentAgent.llmConfig.temperature
                    maxOutputTokens   = $currentAgent.llmConfig.maxOutputTokens
                    enableChatHistory = $currentAgent.llmConfig.enableChatHistory
                }
            }

            try {
                $updateResp = Invoke-Api -Method PUT -Path "/api/agents/$opsAgentId" -Body $updateBody
                if ($updateResp.success) {
                    Write-Ok "Rollback SOP bound to ops-agent"
                } else {
                    Write-Warn "Binding returned: $($updateResp | ConvertTo-Json -Compress)"
                }
            } catch {
                Write-Warn "Failed to bind SOP: $($_.Exception.Message)"
            }
        } else {
            Write-Ok "Rollback SOP already bound to ops-agent"
        }
    }
}

# ──────────── 4. Register/Update Alert Rules ────────────
Write-Step "Registering alert rules ..."

# Get existing rules
$existingRulesResp = Invoke-Api -Path "/api/alert-rules"
$existingRules = $existingRulesResp.data

function Find-ExistingRule($ruleName) {
    ($existingRules | Where-Object { $_.name -eq $ruleName }) | Select-Object -First 1
}

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

# 如果指定了 OverrideHighErrorRate，将现有 HighErrorRate 规则改为使用回滚 SOP
if ($OverrideHighErrorRate -and $rollbackSopId) {
    Write-Info "Updating HighErrorRate rule to use deployment-rollback-response SOP ..."
    $rule1 = @{
        name               = "HighErrorRate"
        description        = "HTTP 5xx 错误率超过 5% 持续 2 分钟 → 自动执行 deployment-rollback-response SOP (含自动回滚)"
        severity           = "P1"
        matchers           = @( @{ label = "alertname"; operator = "Eq"; value = "HighErrorRate" } )
        sopId              = $rollbackSopId
        responderAgentId   = $opsAgentId
        cooldownMinutes    = 5
        contextProviders   = @(
            @{ category = "Metrics"; expression = 'sum(rate(http_requests_total{namespace="${namespace}", status=~"5.."}[2m])) by (app) / sum(rate(http_requests_total{namespace="${namespace}"}[2m])) by (app)'; label = "各服务 5xx 错误率"; lookback = "15m" },
            @{ category = "Metrics"; expression = 'sum(rate(http_requests_total{namespace="${namespace}"}[2m])) by (app)'; label = "各服务 QPS"; lookback = "15m" },
            @{ category = "Logs"; expression = '{namespace="${namespace}"} |= "error"'; label = "错误日志"; lookback = "5m" },
            @{ category = "Deployment"; expression = 'pods/${namespace}'; label = "Pod 状态" }
        )
    }
    try {
        Upsert-AlertRule "HighErrorRate" $rule1 | Out-Null
    } catch { Write-Err "HighErrorRate update — $($_.Exception.Message)" }
}

# 注册独立的 DeploymentErrorSpike 规则 (不覆盖已有的 HighErrorRate)
if ($rollbackSopId) {
    $rule2 = @{
        name               = "DeploymentErrorSpike"
        description        = "部署后错误率急剧飙升 (>30%) → 自动执行部署回滚 SOP; 匹配 HighErrorRate 告警名"
        severity           = "P1"
        matchers           = @( @{ label = "alertname"; operator = "Eq"; value = "HighErrorRate" } )
        sopId              = $rollbackSopId
        responderAgentId   = $opsAgentId
        cooldownMinutes    = 5
        contextProviders   = @(
            @{ category = "Metrics"; expression = 'sum(rate(http_requests_total{namespace="${namespace}", status=~"5.."}[2m])) by (app) / sum(rate(http_requests_total{namespace="${namespace}"}[2m])) by (app)'; label = "各服务 5xx 错误率"; lookback = "15m" },
            @{ category = "Metrics"; expression = 'sum(rate(http_requests_total{namespace="${namespace}"}[2m])) by (app)'; label = "各服务 QPS"; lookback = "15m" },
            @{ category = "Logs"; expression = '{namespace="${namespace}"} |= "error"'; label = "错误日志"; lookback = "5m" },
            @{ category = "Deployment"; expression = 'pods/${namespace}'; label = "Pod 状态" }
        )
    }
    try {
        Upsert-AlertRule "DeploymentErrorSpike" $rule2 | Out-Null
    } catch { Write-Err "DeploymentErrorSpike — $($_.Exception.Message)" }
}

# ──────────── Summary ────────────
Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White
Write-Host "  Rollback Demo Data Seeding Complete!" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White
Write-Host ""
Write-Host "  Created resources:" -ForegroundColor White
Write-Host "    SOP:   deployment-rollback-response (id=$rollbackSopId)" -ForegroundColor Gray
Write-Host "    Rule:  DeploymentErrorSpike → rollback SOP" -ForegroundColor Gray
if ($OverrideHighErrorRate) {
    Write-Host "    Rule:  HighErrorRate updated → rollback SOP" -ForegroundColor Gray
}
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor Yellow
Write-Host "    1. Run '.\demo\rollback-demo.ps1' to execute the full demo" -ForegroundColor White
Write-Host "    2. Or use -OverrideHighErrorRate to make HighErrorRate also use rollback SOP" -ForegroundColor White
Write-Host ""
