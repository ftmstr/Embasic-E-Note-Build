param([ValidateRange(0,100)][int]$ProgressPercent = 36)
# Tur14: static verified GitHub Actions snapshot, not live monitoring.
try { $Host.UI.RawUI.WindowTitle = 'Embasic E-Note | TUR 14' } catch {}
$filled = [int][Math]::Floor($ProgressPercent / 5)
$bar = ('#' * $filled) + ('.' * (20 - $filled))
Write-Host ''
Write-Host '=======================================================================' -ForegroundColor Cyan
Write-Host '                       EMBASIC E-NOTE / TUR 14                       ' -ForegroundColor Cyan
Write-Host '                       WINDOWS DEVELOPMENT                           ' -ForegroundColor Cyan
Write-Host '=======================================================================' -ForegroundColor Cyan
Write-Host ('  ENGINEERING ESTIMATE     {0,3}%  [{1}]' -f $ProgressPercent, $bar) -ForegroundColor Yellow
Write-Host ''
Write-Host '  Windows Release           PASS / 0 warnings / 0 errors' -ForegroundColor Green
Write-Host '  Tur13 and earlier         345/345 PASS' -ForegroundColor Green
Write-Host '  Tur14 nested preview      28/28 PASS' -ForegroundColor Green
Write-Host '  ALL AUTOMATED             373/373 PASS' -ForegroundColor Green
Write-Host '  Verified code run         42259573972' -ForegroundColor Cyan
Write-Host '  Historical CI failures    CS1628 / FIXED' -ForegroundColor Yellow
Write-Host ''
Write-Host '  Nested Markdown           EXPERIMENTAL INERT PREVIEW' -ForegroundColor Cyan
Write-Host '  Escaped tables            STRUCTURED CELLS / TESTED' -ForegroundColor Cyan
Write-Host '  RTF + DOCX/ODT            LIMITED SAFE COPY-ONLY' -ForegroundColor Yellow
Write-Host '  True Word/UYAP WYSIWYG    NOT IMPLEMENTED' -ForegroundColor Yellow
Write-Host '  Secure New Note (.emd)    DISABLED' -ForegroundColor Red
Write-Host '  GUI/DPI/accessibility     NOT RUN' -ForegroundColor Yellow
Write-Host '  ACL/full disk/TOCTOU      NOT RUN' -ForegroundColor Yellow
Write-Host '  Different-user DPAPI      NOT RUN' -ForegroundColor Yellow
Write-Host '  UDF in real UYAP Editor   NOT RUN' -ForegroundColor Yellow
Write-Host '  Signed Windows installer  NOT BUILT' -ForegroundColor Yellow
Write-Host '  V1.0 RELEASE GATE         BLOCKED' -ForegroundColor Red
Write-Host ''
Write-Host '  Conditional planning      2027 Q2-Q3 / NOT A COMMITMENT' -ForegroundColor Cyan
Write-Host '  https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42259573972'
Write-Host '  36% is NOT a measured completion/security acceptance percentage.' -ForegroundColor DarkGray
Write-Host ''
