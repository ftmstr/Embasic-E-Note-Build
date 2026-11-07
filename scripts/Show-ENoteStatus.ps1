param([ValidateRange(0,100)][int]$ProgressPercent = 39)
# Tur 16: historical verified CI snapshot only; not network polling.
try { $Host.UI.RawUI.WindowTitle = 'Embasic E-Note | TUR 16' } catch {}
$filled = [int][Math]::Floor($ProgressPercent / 5)
$bar = ('#' * $filled) + ('.' * (20-$filled))
Write-Host ''
Write-Host '=====================================================================' -ForegroundColor Cyan
Write-Host '                       EMBASIC E-NOTE / TUR 16                    ' -ForegroundColor Cyan
Write-Host '=====================================================================' -ForegroundColor Cyan
Write-Host ('  ENGINEERING ESTIMATE     {0,3}%  [{1}]' -f $ProgressPercent,$bar) -ForegroundColor Yellow
Write-Host ''
Write-Host '  Windows .NET10 Release    PASS / 0 errors / 0 warnings' -ForegroundColor Green
Write-Host '  Tur15 + earlier          418/418 PASS' -ForegroundColor Green
Write-Host '  Tur16 vault pin tests     21/21 PASS' -ForegroundColor Green
Write-Host '  TOTAL                    439/439 PASS' -ForegroundColor Green
Write-Host '  Verified code CI         42411730279' -ForegroundColor Cyan
Write-Host ''
Write-Host '  Pinned vault read        EXPERIMENTAL / OPT-IN' -ForegroundColor Yellow
Write-Host '  Historical ELV1 rollback STILL EXPLOITABLE THROUGH LEGACY API' -ForegroundColor Red
Write-Host '  Trusted monotonic epoch  NOT IMPLEMENTED' -ForegroundColor Red
Write-Host '  Old vault copies         STILL DECRYPTABLE WITH OLD PASSWORD' -ForegroundColor Red
Write-Host '  EMD secure note in WPF   DISABLED' -ForegroundColor Red
Write-Host '  RTF / DOCX / ODT         LIMITED GUARDED COPY-ONLY' -ForegroundColor Yellow
Write-Host '  Native WPF GUI / DPI     NOT RUN' -ForegroundColor Yellow
Write-Host '  NTFS ACL/disk/TOCTOU     NOT RUN' -ForegroundColor Yellow
Write-Host '  Different-user DPAPI     NOT RUN' -ForegroundColor Yellow
Write-Host '  Real UYAP UDF           NOT RUN' -ForegroundColor Yellow
Write-Host '  Signed Windows installer NOT BUILT' -ForegroundColor Yellow
Write-Host ''
Write-Host '  V1.0 RELEASE GATE       BLOCKED' -ForegroundColor Red
Write-Host '  CONDITIONAL V1.0        2027 Q2-Q3 / NOT PROMISED' -ForegroundColor Cyan
Write-Host '  https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42411730279'
Write-Host '  39% is NOT measured product completion or security acceptance.' -ForegroundColor DarkGray
Write-Host ''
