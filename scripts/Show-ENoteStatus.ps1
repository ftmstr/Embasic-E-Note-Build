param(
    [ValidateRange(0,100)][int]$ProgressPercent = 8
)

# Historical, verified CI snapshot; this script does not query GitHub.
$run = '41383011430'
$done = [int][Math]::Floor($ProgressPercent / 5)
$bar = ('█' * $done) + ('░' * (20 - $done))
try { $Host.UI.RawUI.WindowTitle = 'Embasic E-Note | TUR 03' } catch {}
Write-Host ''
Write-Host '╔══════════════════════════════════════════════════════════════╗' -ForegroundColor Cyan
Write-Host '║             EMBASIC E-NOTE • DEVELOPMENT                    ║' -ForegroundColor Cyan
Write-Host '║                     TUR 03 / 2026-11-02                     ║' -ForegroundColor Cyan
Write-Host '╚══════════════════════════════════════════════════════════════╝' -ForegroundColor Cyan
Write-Host ('  Progress  {0}%  {1}' -f $ProgressPercent,$bar) -ForegroundColor Yellow
Write-Host ''
Write-Host ('  Windows CI (historical run {0})        PASS' -f $run) -ForegroundColor Green
Write-Host '  WPF Release build                    0 errors / 0 warnings' -ForegroundColor Green
Write-Host '  Workspace regression tests            17 / 17 PASS' -ForegroundColor Green
Write-Host '  Experimental EMD crypto tests          15 / 15 PASS' -ForegroundColor Green
Write-Host '  Real light/dark Embasic logos          SOURCE INTEGRATED' -ForegroundColor Cyan
Write-Host '  Windows GUI / DPI visual acceptance    NOT RUN' -ForegroundColor Yellow
Write-Host '  Portable Argon2id password mode        NOT IMPLEMENTED' -ForegroundColor Yellow
Write-Host '  EMD create/open/save in application    DISABLED' -ForegroundColor Yellow
Write-Host '  UDF real UYAP editor acceptance        NOT RUN' -ForegroundColor Yellow
Write-Host '  Installer & user acceptance            NOT RUN' -ForegroundColor Yellow
Write-Host ''
Write-Host '  Conditional v1.0 planning window       2027 Q2–Q3' -ForegroundColor Cyan
Write-Host '  RELEASE GATE                           BLOCKED' -ForegroundColor Red
Write-Host ''
Write-Host ('  CI evidence: https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/{0}' -f $run)
Write-Host '  The percentage is an estimate, not a certified completion measure.' -ForegroundColor DarkGray
Write-Host '  Last confirmed metrics refer only to the specified historical CI run.' -ForegroundColor DarkGray
Write-Host ''
