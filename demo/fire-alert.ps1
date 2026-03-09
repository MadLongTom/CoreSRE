<#
.SYNOPSIS
    触发模拟告警到 CoreSRE — 通过 Alertmanager Webhook 端点触发事件响应
.DESCRIPTION
    模拟 Alertmanager 发送告警到 CoreSRE Webhook 端点，触发自动化事件响应。
    支持 3 种告警类型: HighErrorRate / HighLatency / ServiceDown
.PARAMETER Type
    告警类型: HighErrorRate | HighLatency | ServiceDown (default: HighErrorRate)
.PARAMETER Service
    告警关联的服务名 (default: order-service)
.PARAMETER ApiBase
    CoreSRE API 基础地址 (default: http://localhost:5156)
.PARAMETER Resolve
    发送 resolved 状态告警（用于自动关闭 incident）
.EXAMPLE
    .\fire-alert.ps1                                    # 触发 HighErrorRate (order-service)
    .\fire-alert.ps1 -Type HighLatency                  # 触发 HighLatency
    .\fire-alert.ps1 -Type ServiceDown -Service payment-service   # 支付服务宕机
    .\fire-alert.ps1 -Type HighErrorRate -Resolve       # 发送恢复通知
#>
[CmdletBinding()]
param(
    [ValidateSet("HighErrorRate", "HighLatency", "ServiceDown")]
    [string]$Type = "HighErrorRate",

    [string]$Service = "order-service",

    [string]$ApiBase = "http://localhost:5156",

    [switch]$Resolve
)

$ErrorActionPreference = "Stop"

function Write-Step($msg)  { Write-Host "`n▶ $msg" -ForegroundColor Cyan }
function Write-Ok($msg)    { Write-Host "  ✓ $msg" -ForegroundColor Green }
function Write-Err($msg)   { Write-Host "  ✗ $msg" -ForegroundColor Red }
function Write-Info($msg)  { Write-Host "  ℹ $msg" -ForegroundColor Gray }

# ──────────── Resolve Alertmanager DataSource ID ────────────
Write-Step "Resolving Alertmanager DataSource ..."

try {
    $dsResp = Invoke-RestMethod -Uri "$ApiBase/api/datasources" -TimeoutSec 5
} catch {
    Write-Err "Cannot reach CoreSRE API at $ApiBase"
    exit 1
}

$alertmanagerDs = ($dsResp.data.items | Where-Object { $_.name -eq "k8s-alertmanager" }) | Select-Object -First 1
if (-not $alertmanagerDs) {
    # Fallback: try non-paginated format
    $alertmanagerDs = ($dsResp.data | Where-Object { $_.name -eq "k8s-alertmanager" }) | Select-Object -First 1
}
if (-not $alertmanagerDs) {
    Write-Err "Alertmanager DataSource 'k8s-alertmanager' not found. Run deploy-demo.ps1 first."
    exit 1
}

$dsId = $alertmanagerDs.id
Write-Ok "Alertmanager DataSource (id=$dsId)"

# ──────────── Build Alert Payload ────────────
$status = if ($Resolve) { "resolved" } else { "firing" }
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
$fingerprint = "$Type-$Service".GetHashCode().ToString("x8")

$alertConfigs = @{
    HighErrorRate = @{
        severity    = "warning"
        summary     = "$Service HTTP 5xx 错误率超过 10%"
        description = "$Service 在过去 5 分钟内 HTTP 5xx 错误率达 12.5%，超过 5% 阈值。影响范围: $Service 及其上游调用方。"
        value       = "0.125"
    }
    HighLatency = @{
        severity    = "warning"
        summary     = "$Service P99 延迟超过 1 秒"
        description = "$Service P99 延迟达到 2.3 秒，远超 1 秒阈值。可能原因: 下游服务响应慢或资源争用。"
        value       = "2.3"
    }
    ServiceDown = @{
        severity    = "critical"
        summary     = "$Service 服务不可用"
        description = "$Service 的所有 Pod 均不可达，up 指标为 0，持续超过 1 分钟。需要立即处理。"
        value       = "0"
    }
}

$cfg = $alertConfigs[$Type]

$alertPayload = @{
    version  = "4"
    status   = $status
    receiver = "coresre-webhook"
    alerts   = @(
        @{
            status      = $status
            fingerprint = $fingerprint
            labels      = @{
                alertname = $Type
                severity  = $cfg.severity
                namespace = "demo-app"
                app       = $Service
                service   = $Service
                job       = $Service
                instance  = "$($Service):8080"
            }
            annotations = @{
                summary     = $cfg.summary
                description = $cfg.description
                value       = $cfg.value
            }
            startsAt     = $now
            generatorURL = "http://localhost:30090/graph?g0.expr=up"
        }
    )
    groupLabels   = @{ alertname = $Type }
    commonLabels  = @{ namespace = "demo-app"; severity = $cfg.severity }
    externalURL   = "http://localhost:30093"
} | ConvertTo-Json -Depth 10

# ──────────── Fire Alert ────────────
$statusEmoji = if ($Resolve) { "🟢 RESOLVED" } else { "🔴 FIRING" }
Write-Step "Firing alert: $Type ($statusEmoji)"
Write-Info "Service: $Service"
Write-Info "Severity: $($cfg.severity)"
Write-Info "Summary: $($cfg.summary)"

$webhookUrl = "$ApiBase/api/datasources/webhook/$dsId"
Write-Info "Webhook: $webhookUrl"

try {
    $resp = Invoke-RestMethod `
        -Uri $webhookUrl `
        -Method Post `
        -ContentType "application/json; charset=utf-8" `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($alertPayload)) `
        -TimeoutSec 30

    if ($resp.success) {
        Write-Ok "Alert dispatched successfully!"

        if ($resp.incidentIds -and $resp.incidentIds.Count -gt 0) {
            Write-Host ""
            Write-Host "  Created Incidents:" -ForegroundColor White
            foreach ($incId in $resp.incidentIds) {
                Write-Host "    → $ApiBase/api/incidents/$incId" -ForegroundColor White
                Write-Host "    → http://localhost:5173/incidents/$incId" -ForegroundColor Gray
            }
        }

        if ($resp.ignoredCount -gt 0) {
            Write-Info "Ignored (no matching rule or cooldown): $($resp.ignoredCount)"
        }

        if ($resp.errors -and $resp.errors.Count -gt 0) {
            Write-Host ""
            Write-Host "  Dispatch errors:" -ForegroundColor Yellow
            foreach ($e in $resp.errors) {
                Write-Host "    ⚠ $e" -ForegroundColor Yellow
            }
        }
    } else {
        Write-Err "API returned failure"
        Write-Host ($resp | ConvertTo-Json -Depth 5) -ForegroundColor Red
    }
} catch {
    Write-Err "Failed to send webhook: $($_.Exception.Message)"
    $statusCode = $_.Exception.Response.StatusCode.value__
    if ($statusCode) {
        Write-Err "HTTP Status: $statusCode"
    }
}

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White
Write-Host "  What to do next:" -ForegroundColor Yellow
Write-Host "    1. Open Incidents page: http://localhost:5173/incidents" -ForegroundColor White
Write-Host "    2. Watch the automated SOP execution in real-time" -ForegroundColor White
Write-Host "    3. To resolve: .\fire-alert.ps1 -Type $Type -Resolve" -ForegroundColor White
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White
Write-Host ""
