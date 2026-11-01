param(
    [ValidateRange(0,100)][int]$ProgressPercent = 2,
    [ValidateSet('NOT RUN','QUEUED','PASS','FAIL')][string]$CiState = 'NOT RUN'
)

$Host.UI.RawUI.WindowTitle = 'Embasic E-Note — Delivery Status'
$filled = [int][Math]::Floor($ProgressPercent / 5)
$bar = ('█' * $filled) + ('░' * (20 - $filled))
Write-Host ''
Write-Host '╔══════════════════════════════════════════════════════════════╗' -ForegroundColor Cyan
Write-Host '║                   EMBASIC  E-NOTE                            ║' -ForegroundColor Cyan
Write-Host '║                 WINDOWS DEVELOPMENT                          ║' -ForegroundColor Cyan
Write-Host '╚══════════════════════════════════════════════════════════════╝' -ForegroundColor Cyan
Write-Host ('  Estimated implementation progress : {0,3}%  {1}' -f $ProgressPercent,$bar) -ForegroundColor Yellow
Write-Host ('  Windows CI evidence              : {0}' -f $CiState)
Write-Host '  Current milestone                : TUR 01 — WPF bootstrap'
Write-Host '  Encrypted EMD / key recovery      : NOT IMPLEMENTED'
Write-Host '  UYAP UDF editor acceptance       : NOT RUN'
Write-Host '  GUI / installer acceptance       : NOT RUN'
Write-Host '  Provisional release window       : 2027 Q2–Q3 (NOT COMMITTED)'
Write-Host '  Release gate                     : BLOCKED until all P0 tests pass' -ForegroundColor Yellow
Write-Host ''
Write-Host 'Progress is a rough engineering estimate, NOT test coverage or acceptance.' -ForegroundColor DarkGray
