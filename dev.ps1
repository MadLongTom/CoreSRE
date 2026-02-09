# CoreSRE - 一键启动前后端开发环境 (PowerShell)
# 后端通过 Aspire AppHost 启动（自动编排 PostgreSQL + API + Dashboard）
Write-Host ""
Write-Host "🚀 CoreSRE - Starting Development Environment" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

$root = $PSScriptRoot

# 启动后端 (Aspire AppHost: PostgreSQL 容器 + API + Aspire Dashboard)
Write-Host "▶ Starting Backend via Aspire AppHost ..." -ForegroundColor Green
Write-Host "   PostgreSQL + API + Aspire Dashboard" -ForegroundColor DarkGray
$backend = Start-Process -NoNewWindow -PassThru -FilePath "dotnet" `
    -ArgumentList "run", "--project", (Join-Path $root "Backend\CoreSRE.AppHost")

# 启动前端 (npm 在 Windows 上是 .cmd 脚本，需通过 cmd 调用)
Write-Host "▶ Starting Frontend (Vite) on http://localhost:5173 ..." -ForegroundColor Green
$frontend = Start-Process -NoNewWindow -PassThru -FilePath "cmd.exe" `
    -ArgumentList "/c", "npm run dev" `
    -WorkingDirectory (Join-Path $root "Frontend")

Write-Host ""
Write-Host "✅ All services starting! Press Ctrl+C to stop." -ForegroundColor Yellow
Write-Host "   Aspire Dashboard: https://localhost:17178" -ForegroundColor White
Write-Host "   Frontend:         http://localhost:5173" -ForegroundColor White
Write-Host ""

try {
    # 等待任一进程退出
    while ((-not $backend.HasExited) -and (-not $frontend.HasExited)) {
        Start-Sleep -Milliseconds 500
    }
} catch {
    # Ctrl+C pressed
} finally {
    Write-Host "`n🛑 Shutting down..." -ForegroundColor Red
    if (!$backend.HasExited)  { Stop-Process -Id $backend.Id -Force -ErrorAction SilentlyContinue }
    if (!$frontend.HasExited) { Stop-Process -Id $frontend.Id -Force -ErrorAction SilentlyContinue }
    Write-Host "Done." -ForegroundColor Red
}
