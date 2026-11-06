param([ValidateRange(0,100)][int]$ProgressPercent = 35)
# Static evidence snapshot. This does not poll GitHub or claim a Windows GUI run.
try { $Host.UI.RawUI.WindowTitle = 'Embasic E-Note | TUR 13' } catch {}
$filled = [int][Math]::Floor($ProgressPercent / 5)
$bar = ('█' * $filled) + ('░' * (20-$filled))
Write-Host ''
Write-Host '╔═══════════════════════════════════════════════════════════════════╗' -ForegroundColor Cyan
Write-Host '║                         EMBASIC E-NOTE                          ║' -ForegroundColor Cyan
Write-Host '║                    WINDOWS DEVELOPMENT · TUR 13                 ║' -ForegroundColor Cyan
Write-Host '╚═══════════════════════════════════════════════════════════════════╝' -ForegroundColor Cyan
Write-Host ('  ENGINEERING ESTIMATE     {0,3}%  {1}' -f $ProgressPercent,$bar) -ForegroundColor Yellow
Write-Host ''
Write-Host '  Windows Release          PASS · 0 warnings / 0 errors' -ForegroundColor Green
Write-Host '  Tur12 + earlier          318/318 PASS' -ForegroundColor Green
Write-Host '  Tur13 inline + table      27/27 PASS' -ForegroundColor Green
Write-Host '  TOTAL                    345/345 AUTOMATED PASS' -ForegroundColor Green
Write-Host '  Verified code CI         42173395275' -ForegroundColor Cyan
Write-Host ''
Write-Host '  Markdown bold/italic      INERT WPF RUN PREVIEW' -ForegroundColor Cyan
Write-Host '  Inline code/strike        INERT WPF RUN PREVIEW' -ForegroundColor Cyan
Write-Host '  Escaped table pipes       TESTED BASIC MODEL' -ForegroundColor Cyan
Write-Host '  RTF + DOCX + ODT          LIMITED / GUARDED COPY-ONLY' -ForegroundColor Yellow
Write-Host '  Full Word/UYAP WYSIWYG    NOT IMPLEMENTED' -ForegroundColor Yellow
Write-Host '  Secure .emd in WPF        DISABLED' -ForegroundColor Red
Write-Host '  Windows GUI/DPI           NOT RUN' -ForegroundColor Yellow
Write-Host '  NTFS/ACL/TOCTOU           NOT RUN' -ForegroundColor Yellow
Write-Host '  Real UYAP UDF            NOT RUN' -ForegroundColor Yellow
Write-Host '  Signed Windows installer  NOT BUILT' -ForegroundColor Yellow
Write-Host ''
Write-Host '  CONDITIONAL V1.0         2027 Q2-Q3 · NOT PROMISED' -ForegroundColor Cyan
Write-Host '  RELEASE GATE             BLOCKED' -ForegroundColor Red
Write-Host '  https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42173395275'
Write-Host '  Estimate is NOT a measured acceptance/feature completion percentage.' -ForegroundColor DarkGray
Write-Host ''
