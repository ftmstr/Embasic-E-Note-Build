param(
    [ValidateRange(0,100)][int]$ProgressPercent = 12
)
# Verified historical snapshot only; does not query remote CI or schedule work.
try { $Host.UI.RawUI.WindowTitle = 'Embasic E-Note | TUR 04' } catch {}
$filled = [int][Math]::Floor($ProgressPercent / 5)
$bar = ('█' * $filled) + ('░' * (20 - $filled))

Write-Host ''
Write-Host '╔═════════════════════════════════════════════════════════════════╗' -ForegroundColor Cyan
Write-Host '║                    EMBASIC E-NOTE                             ║' -ForegroundColor Cyan
Write-Host '║               WINDOWS DEVELOPMENT · TUR 04                     ║' -ForegroundColor Cyan
Write-Host '╚═════════════════════════════════════════════════════════════════╝' -ForegroundColor Cyan
Write-Host ('  Estimated engineering progress     {0,3}%  {1}' -f $ProgressPercent,$bar) -ForegroundColor Yellow
Write-Host ''
Write-Host '  Windows CI       Release build PASS · 0 warning · 0 error' -ForegroundColor Green
Write-Host '  Folder Storage   17/17 PASS' -ForegroundColor Green
Write-Host '  EMD1 DPAPI       15/15 PASS' -ForegroundColor Green
Write-Host '  EMD2 Portable    30/30 PASS' -ForegroundColor Green
Write-Host '  Last proven run  41385023814 (historical)' -ForegroundColor Cyan
Write-Host '  Actual Windows GUI / high DPI       NOT RUN' -ForegroundColor Yellow
Write-Host '  Cross-account disaster recovery    NOT RUN' -ForegroundColor Yellow
Write-Host '  ACL / disk-full / power-loss        NOT RUN' -ForegroundColor Yellow
Write-Host '  Independent locks and backup UX    NOT IMPLEMENTED' -ForegroundColor Yellow
Write-Host '  Create Secure Note (.emd)           DISABLED' -ForegroundColor Red
Write-Host '  UDF / Official UYAP Editor          NOT RUN' -ForegroundColor Yellow
Write-Host '  Signed release installer           NOT BUILT' -ForegroundColor Yellow
Write-Host ''
Write-Host '  Conditional planning window        2027 Q2–Q3 (NOT PROMISED)' -ForegroundColor Cyan
Write-Host '  v1.0 RELEASE GATE                   BLOCKED' -ForegroundColor Red
Write-Host '  https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41385023814'
Write-Host '  This progress percentage is an estimate, not a release-readiness score.' -ForegroundColor DarkGray
Write-Host ''
