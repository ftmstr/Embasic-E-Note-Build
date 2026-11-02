param([ValidateRange(0,100)][int]$ProgressPercent = 15)

# Snapshot of verified Tur 05 CI, not an active GitHub status fetch.
try { $Host.UI.RawUI.WindowTitle = 'Embasic E-Note | TUR 05' } catch {}
$filled = [int][Math]::Floor($ProgressPercent / 5)
$bar = ('█' * $filled) + ('░' * (20-$filled))
Write-Host ''
Write-Host '╔══════════════════════════════════════════════════════════════════╗' -ForegroundColor Cyan
Write-Host '║                         EMBASIC E-NOTE                         ║' -ForegroundColor Cyan
Write-Host '║                 WINDOWS DEVELOPMENT • TUR 05                    ║' -ForegroundColor Cyan
Write-Host '╚══════════════════════════════════════════════════════════════════╝' -ForegroundColor Cyan
Write-Host ('  Engineering estimate  {0,3}%  {1}' -f $ProgressPercent,$bar) -ForegroundColor Yellow
Write-Host ''
Write-Host '  Windows CI 41386156447         PASS' -ForegroundColor Green
Write-Host '  Windows Release               0 warnings / 0 errors' -ForegroundColor Green
Write-Host '  Workspace / EMD1 / EMD2       17/17 + 15/15 + 30/30 PASS' -ForegroundColor Green
Write-Host '  Recovery / rotation / gates   35/35 PASS' -ForegroundColor Green
Write-Host '  Encrypted recovery ERB1       EXPERIMENTAL' -ForegroundColor Cyan
Write-Host '  EMD1 -> EMD2 migration        TESTED IN MEMORY ONLY' -ForegroundColor Cyan
Write-Host '  Folder/note cryptographic lock NOT IMPLEMENTED' -ForegroundColor Yellow
Write-Host '  Separate Windows account      NOT RUN' -ForegroundColor Yellow
Write-Host '  NTFS ACL / crash / disk full  NOT RUN' -ForegroundColor Yellow
Write-Host '  Real GUI / DPI / keyboard     NOT RUN' -ForegroundColor Yellow
Write-Host '  Secure note UI               DISABLED' -ForegroundColor Red
Write-Host '  UDF / real UYAP Editor        NOT RUN' -ForegroundColor Yellow
Write-Host '  Windows installer            NOT BUILT' -ForegroundColor Yellow
Write-Host ''
Write-Host '  Conditional V1.0 planning     2027 Q2–Q3 (NO COMMITMENT)' -ForegroundColor Cyan
Write-Host '  Release gate                 BLOCKED' -ForegroundColor Red
Write-Host '  CI: https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41386156447'
Write-Host '  Estimate is not a completed-feature or release-acceptance percentage.' -ForegroundColor DarkGray
Write-Host ''
