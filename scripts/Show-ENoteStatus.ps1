param(
    [ValidateRange(0,100)][int]$ProgressPercent = 5,
    [ValidateSet('PASS','FAIL','PENDING','NOT RUN')]
    [string]$VerifiedCiState = 'PASS'
)

# This is a historical evidence dashboard, not a live GitHub status reader.
try { $Host.UI.RawUI.WindowTitle = 'Embasic E-Note | TUR 02' } catch {}

$completed = [int][Math]::Floor($ProgressPercent / 5)
$bar = ('█' * $completed) + ('░' * (20 - $completed))

Write-Host ''
Write-Host '╔══════════════════════════════════════════════════════════════════╗' -ForegroundColor Cyan
Write-Host '║                       EMBASIC  E-NOTE                          ║' -ForegroundColor Cyan
Write-Host '║                  WINDOWS • GELİŞTİRME PANOSU                    ║' -ForegroundColor Cyan
Write-Host '╚══════════════════════════════════════════════════════════════════╝' -ForegroundColor Cyan
Write-Host ('  Tahmini mühendislik ilerlemesi     % {0,3}   {1}' -f $ProgressPercent,$bar) -ForegroundColor Yellow
Write-Host '  TUR 02       Çalışma alanı + klasör ağacı      REVIEW' -ForegroundColor White
Write-Host ('  Windows CI   Run #41330908174                 {0}' -f $VerifiedCiState) -ForegroundColor Green
Write-Host '  Release      0 warning / 0 error              PASS' -ForegroundColor Green
Write-Host '  Smoke        17 / 17 kontrol                  PASS' -ForegroundColor Green
Write-Host '  EMD          Şifreleme / anahtar yönetimi     NOT IMPLEMENTED' -ForegroundColor Yellow
Write-Host '  GUI          Fiziksel Windows kabulü         NOT RUN' -ForegroundColor Yellow
Write-Host '  UDF          Resmî UYAP aç / kaydet           NOT RUN' -ForegroundColor Yellow
Write-Host '  Kurulum      İmzalı / doğrulanmış dağıtım     NOT BUILT' -ForegroundColor Yellow
Write-Host ''
Write-Host '  Koşullu nihai sürüm aralığı: 2027 Q2 – Q3 (TAHHÜT DEĞİL)' -ForegroundColor Cyan
Write-Host '  Yayın kapısı: BLOCKED — tüm P0 kabul testleri zorunludur.' -ForegroundColor Yellow
Write-Host ''
Write-Host '  CI kanıtı: https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41330908174'
Write-Host '  İlerleme yaklaşık iş yükü tahminidir; test kapsamı veya hazır ürün oranı değildir.' -ForegroundColor DarkGray
Write-Host '  PASS yalnızca yukarıda belirtilen GitHub Actions çalışması içindir.' -ForegroundColor DarkGray
Write-Host ''
