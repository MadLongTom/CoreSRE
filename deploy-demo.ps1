<#
.SYNOPSIS
    CoreSRE 一键部署可观测性数据源 + 模拟分布式业务系统到本地 K8s
.DESCRIPTION
    1. 部署 observability 命名空间: Prometheus, Loki+Promtail, Jaeger, Alertmanager
    2. 部署 demo-app 命名空间: order/payment/inventory 微服务 + traffic-generator
    3. 等待所有 Pod 就绪
    4. 将数据源注册到 CoreSRE 后端 (POST /api/datasources)
.PARAMETER ApiBase
    CoreSRE 后端 API 基础地址 (default: http://localhost:5156)
.PARAMETER SkipDeploy
    跳过 K8s 部署, 仅执行数据源注册
.PARAMETER TearDown
    拆除全部资源
#>
[CmdletBinding()]
param(
    [string]$ApiBase = "http://localhost:5156",
    [switch]$SkipDeploy,
    [switch]$TearDown
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$k8sDir = Join-Path $root "k8s"

# ──────────── Colors ────────────
function Write-Step($msg) { Write-Host "▶ $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  ✓ $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "  ⚠ $msg" -ForegroundColor Yellow }
function Write-Err($msg)  { Write-Host "  ✗ $msg" -ForegroundColor Red }

# ──────────── TearDown ────────────
if ($TearDown) {
    Write-Step "Tearing down observability + demo-app ..."
    kubectl delete namespace demo-app --ignore-not-found 2>$null
    kubectl delete namespace observability --ignore-not-found 2>$null
    kubectl delete clusterrole prometheus promtail --ignore-not-found 2>$null
    kubectl delete clusterrolebinding prometheus promtail --ignore-not-found 2>$null
    Write-Ok "All resources removed."
    exit 0
}

# ──────────── Prerequisites ────────────
Write-Step "Checking prerequisites ..."
$null = kubectl cluster-info 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Err "kubectl cannot connect to cluster. Start Docker Desktop / minikube first."
    exit 1
}
Write-Ok "Kubernetes cluster reachable."

# ──────────── Deploy ────────────
if (-not $SkipDeploy) {

    # 1. Observability namespace
    Write-Step "Creating observability namespace ..."
    kubectl apply -f (Join-Path $k8sDir "observability\namespace.yaml")

    # 2. Observability stack
    Write-Step "Deploying Prometheus ..."
    kubectl apply -f (Join-Path $k8sDir "observability\prometheus.yaml")
    Write-Ok "Prometheus deployed."

    Write-Step "Deploying Loki + Promtail ..."
    kubectl apply -f (Join-Path $k8sDir "observability\loki.yaml")
    Write-Ok "Loki + Promtail deployed."

    Write-Step "Deploying Jaeger ..."
    kubectl apply -f (Join-Path $k8sDir "observability\jaeger.yaml")
    Write-Ok "Jaeger deployed."

    Write-Step "Deploying Alertmanager ..."
    kubectl apply -f (Join-Path $k8sDir "observability\alertmanager.yaml")
    Write-Ok "Alertmanager deployed."

    # 3. Demo App namespace
    Write-Step "Creating demo-app namespace ..."
    kubectl apply -f (Join-Path $k8sDir "demo-app\namespace.yaml")

    # 4. Demo App services
    Write-Step "Deploying simulated business services ..."
    kubectl apply -f (Join-Path $k8sDir "demo-app\services.yaml")
    Write-Ok "order-service, payment-service, inventory-service, traffic-generator deployed."

    # 5. Wait for readiness
    Write-Step "Waiting for observability pods (timeout 180s) ..."
    $obsPods = @("prometheus", "loki", "jaeger", "alertmanager")
    foreach ($pod in $obsPods) {
        Write-Host "    Waiting for $pod ..." -NoNewline
        kubectl rollout status deployment/$pod -n observability --timeout=180s 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Host " Ready" -ForegroundColor Green
        } else {
            Write-Host " TIMEOUT" -ForegroundColor Red
        }
    }
    # Promtail is DaemonSet
    Write-Host "    Waiting for promtail ..." -NoNewline
    kubectl rollout status daemonset/promtail -n observability --timeout=120s 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { Write-Host " Ready" -ForegroundColor Green }
    else { Write-Host " TIMEOUT" -ForegroundColor Red }

    Write-Step "Waiting for demo-app pods (timeout 300s) ..."
    $appPods = @("order-service", "payment-service", "inventory-service")
    foreach ($pod in $appPods) {
        Write-Host "    Waiting for $pod ..." -NoNewline
        kubectl rollout status deployment/$pod -n demo-app --timeout=300s 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Host " Ready" -ForegroundColor Green
        } else {
            Write-Host " TIMEOUT" -ForegroundColor Red
        }
    }

    Write-Host ""
    Write-Step "Deployment summary:"
    kubectl get pods -n observability -o wide 2>$null
    Write-Host ""
    kubectl get pods -n demo-app -o wide 2>$null
}

# ──────────── Seed DataSources into CoreSRE ────────────
Write-Host ""
Write-Step "Registering data sources in CoreSRE ($ApiBase) ..."

# Check API availability
try {
    $null = Invoke-WebRequest -Uri "$ApiBase/api/datasources" -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
    Write-Ok "CoreSRE API is reachable."
} catch {
    Write-Warn "CoreSRE API not reachable at $ApiBase. Run './dev.ps1' first. Skipping seed."
    Write-Host ""
    Write-Host "Manual seed command:" -ForegroundColor Yellow
    Write-Host "  .\deploy-demo.ps1 -SkipDeploy -ApiBase http://localhost:5156"
    exit 0
}

# Helper to create data source
function Register-DataSource {
    param(
        [string]$Name,
        [string]$Description,
        [string]$Category,
        [string]$Product,
        [hashtable]$ConnectionConfig,
        [hashtable]$QueryConfig = $null
    )

    $body = @{
        name             = $Name
        description      = $Description
        category         = $Category
        product          = $Product
        connectionConfig = $ConnectionConfig
    }
    if ($QueryConfig) {
        $body["defaultQueryConfig"] = $QueryConfig
    }

    $json = $body | ConvertTo-Json -Depth 5
    try {
        $resp = Invoke-RestMethod -Uri "$ApiBase/api/datasources" `
            -Method Post -ContentType "application/json" -Body $json -TimeoutSec 10
        if ($resp.success) {
            Write-Ok "$Name (id=$($resp.data.id))"
        } else {
            Write-Warn "$Name — API returned errors: $($resp.errors -join ', ')"
        }
    } catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -eq 409) {
            Write-Warn "$Name — already exists (409 Conflict), skipping."
        } else {
            Write-Err "$Name — $($_.Exception.Message)"
        }
    }
}

# 1. Prometheus (Metrics)
Register-DataSource `
    -Name "k8s-prometheus" `
    -Description "Local Kubernetes Prometheus — scrapes demo-app microservices metrics (http_requests_total, http_request_duration_seconds, orders_total, payments_total, inventory_level)" `
    -Category "Metrics" `
    -Product "Prometheus" `
    -ConnectionConfig @{
        baseUrl        = "http://127.0.0.1:30090"
        authType       = "None"
        timeoutSeconds = 30
        tlsSkipVerify  = $false
    } `
    -QueryConfig @{
        defaultStep   = "15s"
        maxResults    = 1000
        defaultLabels = @{ namespace = "demo-app" }
    }

# 2. Loki (Logs)
Register-DataSource `
    -Name "k8s-loki" `
    -Description "Local Kubernetes Loki — collects structured JSON logs from demo-app microservices via Promtail DaemonSet" `
    -Category "Logs" `
    -Product "Loki" `
    -ConnectionConfig @{
        baseUrl        = "http://127.0.0.1:30100"
        authType       = "None"
        timeoutSeconds = 30
        tlsSkipVerify  = $false
    } `
    -QueryConfig @{
        maxResults    = 500
        defaultLabels = @{ namespace = "demo-app" }
    }

# 3. Jaeger (Tracing)
Register-DataSource `
    -Name "k8s-jaeger" `
    -Description "Local Kubernetes Jaeger — receives OTLP traces from order-service, payment-service, inventory-service with full distributed trace propagation" `
    -Category "Tracing" `
    -Product "Jaeger" `
    -ConnectionConfig @{
        baseUrl        = "http://127.0.0.1:30686"
        authType       = "None"
        timeoutSeconds = 30
        tlsSkipVerify  = $false
    }

# 4. Alertmanager (Alerting)
Register-DataSource `
    -Name "k8s-alertmanager" `
    -Description "Local Kubernetes Alertmanager — receives alerts from Prometheus (HighErrorRate, HighLatency, ServiceDown rules)" `
    -Category "Alerting" `
    -Product "Alertmanager" `
    -ConnectionConfig @{
        baseUrl        = "http://127.0.0.1:30093"
        authType       = "None"
        timeoutSeconds = 30
        tlsSkipVerify  = $false
    }

# 5. Kubernetes (Deployment)
Register-DataSource `
    -Name "k8s-cluster" `
    -Description "Local Docker Desktop Kubernetes cluster — manages demo-app (order/payment/inventory services) and observability (Prometheus/Loki/Jaeger/Alertmanager) workloads" `
    -Category "Deployment" `
    -Product "Kubernetes" `
    -ConnectionConfig @{
        baseUrl        = "https://kubernetes.docker.internal:6443"
        authType       = "None"
        timeoutSeconds = 30
        tlsSkipVerify  = $true
        namespace      = "demo-app"
    } `
    -QueryConfig @{
        defaultNamespace = "demo-app"
    }

Write-Host ""
Write-Step "All done! Access points:"
Write-Host "  Prometheus:    http://localhost:30090" -ForegroundColor White
Write-Host "  Loki:          http://localhost:30100" -ForegroundColor White
Write-Host "  Jaeger UI:     http://localhost:30686" -ForegroundColor White
Write-Host "  Alertmanager:  http://localhost:30093" -ForegroundColor White
Write-Host "  CoreSRE API:   $ApiBase/api/datasources" -ForegroundColor White
Write-Host ""
Write-Host "Useful commands:" -ForegroundColor Yellow
Write-Host "  kubectl get pods -n observability     # 查看可观测性组件"
Write-Host "  kubectl get pods -n demo-app          # 查看业务微服务"
Write-Host "  kubectl logs -n demo-app -l app=order-service -f   # 查看订单服务日志"
Write-Host "  .\deploy-demo.ps1 -TearDown           # 拆除全部资源"
Write-Host ""
