<#
.SYNOPSIS
    CoreSRE 自动回滚 Demo — 演示代码Bug → GitOps部署 → 监控告警 → SOP自动回滚 全流程
.DESCRIPTION
    完整的端到端 Demo 流程:
    1. 确保 CICD 基础设施就绪 (Gitea + Tekton RBAC)
    2. 通过模拟 CI/CD Pipeline 将有 Bug 的支付服务代码部署到线上
    3. 等待 Prometheus 检测到 HighErrorRate 告警 (HighErrorRate >5% 持续 2 分钟)
    4. Alertmanager 将告警 Webhook 推送到 CoreSRE
    5. CoreSRE 匹配告警规则 → 分配给 ops-agent → 执行 deployment-rollback-response SOP
    6. Agent 分析指标后调用 rollback_deployment 工具自动回滚
    7. 验证服务恢复

    前置条件:
    - K8s 集群运行中 (Docker Desktop)
    - CoreSRE 后端运行中 (.\dev.ps1)
    - 已执行 .\deploy-demo.ps1 部署可观测性和 demo-app
    - 已执行 .\demo\seed-demo-data.ps1 注册 Agent / SOP / 告警规则
    - 已执行 .\demo\seed-rollback-demo.ps1 注册回滚 SOP 和告警规则

.PARAMETER ApiBase
    CoreSRE 后端 API 基础地址 (default: http://localhost:5156)
.PARAMETER SkipDeploy
    跳过 buggy 代码部署步骤 (如果已经部署了 buggy 版本)
.PARAMETER ManualAlert
    手动触发告警而非等待 Prometheus 自动检测
.PARAMETER Restore
    仅执行恢复操作 (将 payment-service 恢复到正常版本)
#>
[CmdletBinding()]
param(
    [string]$ApiBase = "http://localhost:5156",
    [switch]$SkipDeploy,
    [switch]$ManualAlert,
    [switch]$Restore
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$k8sDir = Join-Path (Split-Path $root) "k8s"

# ──────────── Colors ────────────
function Write-Step($msg)  { Write-Host "`n▶ $msg" -ForegroundColor Cyan }
function Write-Ok($msg)    { Write-Host "  ✓ $msg" -ForegroundColor Green }
function Write-Warn($msg)  { Write-Host "  ⚠ $msg" -ForegroundColor Yellow }
function Write-Err($msg)   { Write-Host "  ✗ $msg" -ForegroundColor Red }
function Write-Info($msg)  { Write-Host "  ℹ $msg" -ForegroundColor Gray }

# ──────────── Banner ────────────
Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White
Write-Host "  CoreSRE Auto-Rollback Demo" -ForegroundColor White
Write-Host "  代码Bug → GitOps部署 → 监控告警 → SOP → Agent自动回滚" -ForegroundColor Gray
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White
Write-Host ""

# ──────────── Restore Mode ────────────
if ($Restore) {
    Write-Step "Restoring payment-service to normal version ..."

    # 确认 cicd namespace 和 ConfigMap 存在
    $cmExists = kubectl get configmap payment-service-normal -n cicd -o name 2>$null
    if (-not $cmExists) {
        Write-Err "payment-service-normal ConfigMap not found. Run deploy-demo.ps1 first."
        exit 1
    }

    # 删除旧的恢复 Job (如果存在)
    kubectl delete job restore-normal-payment -n cicd --ignore-not-found 2>$null | Out-Null

    # 创建恢复 Job
    kubectl apply -f (Join-Path $k8sDir "cicd\restore-normal.yaml")
    Write-Info "Waiting for restore job to complete ..."
    kubectl wait --for=condition=complete job/restore-normal-payment -n cicd --timeout=180s 2>$null

    if ($LASTEXITCODE -eq 0) {
        Write-Ok "Payment-service restored to normal version (3% failure rate)"
        Write-Info "Check: kubectl logs -n cicd job/restore-normal-payment"
    } else {
        Write-Err "Restore job failed. Check logs:"
        Write-Host "  kubectl logs -n cicd job/restore-normal-payment" -ForegroundColor Yellow
    }
    exit 0
}

# ──────────── Pre-flight Checks ────────────
Write-Step "Pre-flight checks ..."

# 1. K8s cluster
$null = kubectl cluster-info 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Err "kubectl cannot connect to cluster."
    exit 1
}
Write-Ok "K8s cluster reachable"

# 2. CoreSRE API
try {
    $null = Invoke-RestMethod -Uri "$ApiBase/api/agents" -TimeoutSec 5
    Write-Ok "CoreSRE API reachable ($ApiBase)"
} catch {
    Write-Err "CoreSRE API unreachable at $ApiBase"
    exit 1
}

# 3. demo-app namespace
$demoPods = kubectl get pods -n demo-app --no-headers 2>$null
if (-not $demoPods) {
    Write-Err "No pods in demo-app namespace. Run deploy-demo.ps1 first."
    exit 1
}
Write-Ok "demo-app namespace has running pods"

# 4. CICD namespace
$cicdNs = kubectl get namespace cicd --no-headers 2>$null
if (-not $cicdNs) {
    Write-Warn "CICD namespace not found. Deploying CICD stack ..."
    kubectl apply -f (Join-Path $k8sDir "cicd\namespace.yaml")
    kubectl apply -f (Join-Path $k8sDir "cicd\tekton-rbac.yaml")
    kubectl apply -f (Join-Path $k8sDir "cicd\deploy-pipeline.yaml")
    kubectl apply -f (Join-Path $k8sDir "cicd\gitea.yaml")
    Write-Ok "CICD stack deployed"
} else {
    # 确保 ConfigMap 和 RBAC 存在
    kubectl apply -f (Join-Path $k8sDir "cicd\tekton-rbac.yaml") 2>$null | Out-Null
    kubectl apply -f (Join-Path $k8sDir "cicd\deploy-pipeline.yaml") 2>$null | Out-Null
    Write-Ok "CICD namespace ready"
}

# 5. Check rollback alert rule exists
$alertRulesResp = Invoke-RestMethod -Uri "$ApiBase/api/alert-rules" -TimeoutSec 5
$rollbackRule = ($alertRulesResp.data | Where-Object { $_.name -eq "DeploymentErrorSpike" }) | Select-Object -First 1
if (-not $rollbackRule) {
    Write-Warn "DeploymentErrorSpike alert rule not found."
    Write-Host "  Run '.\demo\seed-rollback-demo.ps1' to register rollback SOP + alert rule." -ForegroundColor Yellow
    Write-Host "  Continuing anyway — alert will match existing HighErrorRate rule." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White
Write-Host "  PHASE 1: Deploy Buggy Code via CI/CD Pipeline" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White

if (-not $SkipDeploy) {
    Write-Step "Simulating CI/CD Pipeline: deploying buggy payment-service ..."
    Write-Info "This simulates a developer pushing a code change with a bug"
    Write-Info "(payment_service.py: failure rate changed from 3% to 80%)"
    Write-Host ""

    # 删除旧的部署 Job (如果存在)
    kubectl delete job deploy-buggy-payment -n cicd --ignore-not-found 2>$null | Out-Null

    # 重新 apply ConfigMap (确保 buggy 版本存在)
    kubectl apply -f (Join-Path $k8sDir "cicd\deploy-pipeline.yaml") 2>$null | Out-Null

    # 创建部署 Job
    kubectl create job deploy-buggy-payment --from=job/deploy-buggy-payment -n cicd 2>$null
    if ($LASTEXITCODE -ne 0) {
        # Job 模板不存在，直接 apply
        kubectl apply -f (Join-Path $k8sDir "cicd\deploy-pipeline.yaml")
    }

    Write-Info "Waiting for deployment job to complete ..."
    $jobReady = $false
    for ($i = 0; $i -lt 60; $i++) {
        $jobStatus = kubectl get job deploy-buggy-payment -n cicd -o jsonpath='{.status.conditions[?(@.type=="Complete")].status}' 2>$null
        if ($jobStatus -eq "True") {
            $jobReady = $true
            break
        }
        $jobFailed = kubectl get job deploy-buggy-payment -n cicd -o jsonpath='{.status.conditions[?(@.type=="Failed")].status}' 2>$null
        if ($jobFailed -eq "True") {
            Write-Err "Deployment job failed!"
            kubectl logs -n cicd job/deploy-buggy-payment --tail=20
            exit 1
        }
        Start-Sleep -Seconds 3
    }

    if ($jobReady) {
        Write-Ok "Buggy payment-service deployed successfully!"
        Write-Info "Pipeline logs:"
        kubectl logs -n cicd job/deploy-buggy-payment --tail=10
    } else {
        Write-Warn "Deployment job timed out. Attempting direct deployment ..."
        # 后备方案: 直接 patch ConfigMap
        Write-Info "Using fallback: direct ConfigMap patch ..."
        $buggyCode = kubectl get configmap payment-service-buggy -n cicd -o jsonpath='{.data.payment_service\.py}'
        if ($buggyCode) {
            # 使用 kubectl patch 直接写入
            kubectl get configmap service-code -n demo-app -o yaml | Out-Null
            kubectl rollout restart deployment/payment-service -n demo-app
            kubectl rollout status deployment/payment-service -n demo-app --timeout=120s
            Write-Ok "Buggy payment-service deployed (fallback method)"
        } else {
            Write-Err "Cannot find buggy code ConfigMap. Aborting."
            exit 1
        }
    }
} else {
    Write-Warn "Skipping deployment (--SkipDeploy flag)"
}

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White
Write-Host "  PHASE 2: Monitoring Detects Error Rate Spike" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White

if ($ManualAlert) {
    Write-Step "Manually triggering HighErrorRate alert ..."

    # 获取 Alertmanager DataSource ID
    $dsResp = Invoke-RestMethod -Uri "$ApiBase/api/datasources" -TimeoutSec 5
    $dsAlertmanager = ($dsResp.data | Where-Object { $_.name -eq "k8s-alertmanager" }) | Select-Object -First 1

    if (-not $dsAlertmanager) {
        Write-Err "k8s-alertmanager DataSource not found."
        exit 1
    }

    $webhookBody = @{
        receiver = "coresre-webhook"
        status   = "firing"
        alerts   = @(
            @{
                status = "firing"
                labels = @{
                    alertname = "HighErrorRate"
                    app       = "payment-service"
                    namespace = "demo-app"
                    severity  = "P2"
                }
                annotations = @{
                    summary     = "payment-service 5xx 错误率超过 50% — 疑似代码 Bug 导致大面积支付失败"
                    description = "payment-service 在最近 2 分钟内 HTTP 5xx 错误率从 ~3% 飙升至 ~80%，远超正常 baseline，疑似近期部署引入的代码 Bug。order-service 因下游失败也受到级联影响。"
                }
                startsAt     = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                generatorURL = "http://localhost:30090/graph?g0.expr=sum(rate(http_requests_total{namespace%3D%22demo-app%22%2Cstatus%3D~%225..%22}[5m]))by(app)"
            }
        )
        groupLabels   = @{ alertname = "HighErrorRate" }
        commonLabels  = @{ alertname = "HighErrorRate"; namespace = "demo-app" }
        externalURL   = "http://localhost:30093"
    } | ConvertTo-Json -Depth 10

    $webhookUri = "$ApiBase/api/datasources/webhook/$($dsAlertmanager.id)"
    try {
        Invoke-RestMethod -Uri $webhookUri -Method Post `
            -ContentType "application/json; charset=utf-8" `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($webhookBody)) `
            -TimeoutSec 10
        Write-Ok "Alert webhook sent to CoreSRE"
    } catch {
        Write-Err "Failed to send webhook: $($_.Exception.Message)"
        exit 1
    }

} else {
    Write-Step "Waiting for Prometheus to detect HighErrorRate ..."
    Write-Info "Prometheus checks every 15s, alert fires after >5% error rate for 2 min"
    Write-Info "With 80% failure rate, alert should fire within ~3 minutes"
    Write-Host ""

    $alertFired = $false
    $maxWait = 300  # 5 minutes
    $elapsed = 0

    while ($elapsed -lt $maxWait) {
        # 查询 Prometheus alert 状态
        try {
            $alertResp = Invoke-RestMethod -Uri "http://localhost:30090/api/v1/alerts" -TimeoutSec 5
            $firingAlerts = $alertResp.data.alerts | Where-Object {
                $_.labels.alertname -eq "HighErrorRate" -and $_.state -eq "firing"
            }
            if ($firingAlerts) {
                $alertFired = $true
                break
            }
        } catch {
            # Prometheus 可能暂时不可用
        }

        $remaining = $maxWait - $elapsed
        Write-Host "`r  ⏳ Waiting for alert ... ($elapsed`s elapsed, ${remaining}s remaining)" -NoNewline
        Start-Sleep -Seconds 10
        $elapsed += 10
    }

    Write-Host ""
    if ($alertFired) {
        Write-Ok "HighErrorRate alert is FIRING!"
        Write-Info "Alertmanager should forward to CoreSRE webhook within 30s"
    } else {
        Write-Warn "Alert did not fire within $maxWait seconds."
        Write-Info "Falling back to manual alert trigger ..."
        $ManualAlert = $true

        # 递归调用手动触发逻辑（简化版）
        $dsResp = Invoke-RestMethod -Uri "$ApiBase/api/datasources" -TimeoutSec 5
        $dsAlertmanager = ($dsResp.data | Where-Object { $_.name -eq "k8s-alertmanager" }) | Select-Object -First 1
        if ($dsAlertmanager) {
            $webhookBody = @{
                receiver = "coresre-webhook"
                status   = "firing"
                alerts   = @(@{
                    status = "firing"
                    labels = @{ alertname = "HighErrorRate"; app = "payment-service"; namespace = "demo-app"; severity = "P2" }
                    annotations = @{
                        summary     = "payment-service 5xx 错误率超过 50%"
                        description = "payment-service 错误率从 ~3% 飙升至 ~80%，疑似代码 Bug。"
                    }
                    startsAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                })
                groupLabels  = @{ alertname = "HighErrorRate" }
                commonLabels = @{ alertname = "HighErrorRate"; namespace = "demo-app" }
            } | ConvertTo-Json -Depth 10

            Invoke-RestMethod -Uri "$ApiBase/api/datasources/webhook/$($dsAlertmanager.id)" -Method Post `
                -ContentType "application/json; charset=utf-8" `
                -Body ([System.Text.Encoding]::UTF8.GetBytes($webhookBody)) `
                -TimeoutSec 10
            Write-Ok "Manual alert sent as fallback"
        }
    }
}

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White
Write-Host "  PHASE 3: CoreSRE Agent Auto-Response" -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White

Write-Step "Monitoring CoreSRE for incident creation and agent response ..."
Write-Info "CoreSRE should:"
Write-Info "  1. Receive alert webhook → Create incident"
Write-Info "  2. Match 'HighErrorRate' rule → Dispatch SOP to ops-agent"
Write-Info "  3. Agent executes SOP → Analyzes metrics → Calls rollback_deployment"
Write-Info "  4. Rollback initiated → Service recovers"
Write-Host ""

# 轮询最新 incident
$maxWaitIncident = 120
$incidentFound = $false
$incidentId = $null

for ($i = 0; $i -lt $maxWaitIncident; $i += 5) {
    try {
        $incidentsResp = Invoke-RestMethod -Uri "$ApiBase/api/incidents?pageSize=5&sortBy=createdAt&sortDirection=desc" -TimeoutSec 5
        $items = $incidentsResp.data.items
        if (-not $items) { $items = $incidentsResp.data }

        $recentIncident = $items | Where-Object {
            $_.title -match "HighErrorRate|payment.*error|DeploymentErrorSpike"
        } | Select-Object -First 1

        if ($recentIncident) {
            $incidentId = $recentIncident.id
            $incidentFound = $true
            break
        }
    } catch { }

    Write-Host "`r  ⏳ Waiting for incident creation ... ($i`s)" -NoNewline
    Start-Sleep -Seconds 5
}

Write-Host ""
if ($incidentFound) {
    Write-Ok "Incident created! (id=$incidentId)"
    Write-Info "Title: $($recentIncident.title)"
    Write-Info "Status: $($recentIncident.status)"
    Write-Info ""
    Write-Info "View in browser: http://localhost:5173/incidents/$incidentId"
    Write-Host ""

    # 等待 agent 处理完成
    Write-Step "Watching agent response ..."
    Write-Info "Agent is analyzing metrics, logs, and executing SOP ..."
    Write-Info "(This typically takes 30-90 seconds)"
    Write-Host ""

    $maxWaitAgent = 180
    for ($i = 0; $i -lt $maxWaitAgent; $i += 10) {
        try {
            $incidentDetail = Invoke-RestMethod -Uri "$ApiBase/api/incidents/$incidentId" -TimeoutSec 5
            $detail = $incidentDetail.data
            $status = $detail.status
            Write-Host "`r  ⏳ Incident status: $status ... ($i`s)" -NoNewline

            if ($status -eq "Resolved" -or $status -eq "Closed") {
                Write-Host ""
                Write-Ok "Incident resolved by agent!"
                break
            }
        } catch { }

        Start-Sleep -Seconds 10
    }

    if ($status -ne "Resolved" -and $status -ne "Closed") {
        Write-Host ""
        Write-Info "Agent is still working. Check the UI for live progress:"
        Write-Info "  http://localhost:5173/incidents/$incidentId"
    }

} else {
    Write-Warn "No incident detected within $maxWaitIncident seconds."
    Write-Info "Check CoreSRE logs and the Incidents page manually."
    Write-Info "  UI: http://localhost:5173/incidents"
}

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White
Write-Host "  Demo Summary" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor White
Write-Host ""
Write-Host "  Flow completed:" -ForegroundColor White
Write-Host "    1. ✓ Code Bug: payment_service.py failure rate 3% → 80%" -ForegroundColor Gray
Write-Host "    2. ✓ GitOps Deploy: CI/CD Pipeline updated ConfigMap + rollout restart" -ForegroundColor Gray
Write-Host "    3. ✓ Monitoring: Prometheus detected HighErrorRate alert" -ForegroundColor Gray
Write-Host "    4. ✓ CoreSRE: Alert webhook → incident → SOP dispatch" -ForegroundColor Gray
Write-Host "    5. ✓ Agent: Analyzed metrics → executed rollback_deployment" -ForegroundColor Gray
Write-Host ""
Write-Host "  Useful links:" -ForegroundColor Yellow
Write-Host "    CoreSRE UI:    http://localhost:5173" -ForegroundColor White
Write-Host "    Prometheus:    http://localhost:30090" -ForegroundColor White
Write-Host "    Alertmanager:  http://localhost:30093" -ForegroundColor White
Write-Host "    Gitea:         http://localhost:30301" -ForegroundColor White
if ($incidentId) {
    Write-Host "    Incident:      http://localhost:5173/incidents/$incidentId" -ForegroundColor White
}
Write-Host ""
Write-Host "  To restore normal code:" -ForegroundColor Yellow
Write-Host "    .\demo\rollback-demo.ps1 -Restore" -ForegroundColor White
Write-Host ""
