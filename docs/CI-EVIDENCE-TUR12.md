# Tur 12 — Public Windows CI ve güvenlik kanıtları (06.11.2026)

## Başlangıç ve gerçek depo sırası
Tek kaynak: Public `ftmstr/Embasic-E-Note-Build`, `main` HEAD
`a10faf48e7cd153a53803192e0ac7de39c998458` değişmedi.
PR #9 `fe0030e2d8dbb3e972a7d3e0f59ce4c691ee21ca` Draft.
PR #10 `facad8dd09d5df0ba705bc369bacdd11ef1a40d8` Draft:
native RTF copy-only, 16 regresyon testi.
PR #11 `849887b5f27088b2e836af687780f7aacecca339` Draft:
sınırlı, fail-closed DOCX/ODT metin motoru, 32 test.
Eski paralel PR #12 Tur 09 tabanlı olduğundan RTF/DOCX/ODT kodlarını
barındırmıyordu; Tur 12 entegrasyonu PR #13'e aktarılarak PR #12
superseded olarak kapatılacak.

## Değişiklik commit'leri ve amaçları
- `d4893523d3db0f91620f78ff0b9bb95de4de5f68`:
  Tur 11 HEAD üzerinde, güvenli `MarkdownPreviewParser` ve
  `Tur12.Tests` (21 test), bağımsız proje/Solution/Windows CI.
- `aef7cdfe55b5c80653501c1771a8d37c6fc020bc`:
  WPF Write/Preview/Split + `FlowDocument`, gecikmeli yenileme,
  RTF ve DOCX/ODT işlevlerini kaybetmeden Tur 11 XAML üzerinde
  üç-yollu kaynak bazlı birleştirme.
- Son dokümantasyon commit'i: README, NOTICE, Tur12 kanıtı,
  Araştırma/Tur12 şartname ve PowerShell durum panosu.

## Gerçek Windows Actions
[42111579856](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42111579856):
parser ilk commit için SUCCESS. 
[42111850623](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/42111850623):
WPF birleştirme HEAD için Windows .NET 10 Release **0 Warning(s),
0 Error(s)**, şu otomatik kontrollerin tamamı PASS:
- Workspace ve sayfa geometrisi 31/31
- EMD1 15/15; EMD2/Storage 30/30
- Recovery/rotation 35/35; Tur06 38/38; Tur07 42/42
- Tur08 27/27; Tur09 31/31
- Tur10 RTF 16/16; Tur11 DOCX/ODT 32/32
- Tur12 inert Markdown preview 21/21
**TOPLAM 318/318**; gerçek Windows Actions job `127541141707`
loglarının içerikleri okundu, Build succeeded.
Bu bir Windows CI HEADLESS WPF derlemesi ve konsol testidir,
**Windows GUI otomasyon/manuel kabulü değildir**.

## Negatif testler ve sınırlar
21 yeni parser testi: Unicode/Türkçe/Japonca, 6 başlık seviyesi,
sıralı/sırasız liste, temel tablo ve hatalı tablo, code fence,
kapanmamış fence, alıntı, CRLF, HTML/script/URL/görselin literal
gösterimi, karakter/satır sınırı ve kaynak değişmezliği.
FlowDocument renderer test edilmedi: WPF derleme PASS ancak gerçek
Windows pencerede render/PDF/DPI/klavye NOT RUN.
Açık .md ve layout sidecar şifresizdir. EMD kilitleri ve ELH1
prototipleri WPF'de güvenli not kaydına bağlanmadı.
Gerçek NTFS ACL, disk full, power loss, symlink/TOCTOU, cross-user
DPAPI, root recovery, old vault rollback, kötü amaçlı lokal yazılım,
çoklu proses atomik CAS: NOT RUN / çözülmedi.
UDF gerçek UYAP doküman editöründe imzasız ve düzenlenebilir
açılış/yeniden kayıt/fidelity: NOT RUN. Installer: NOT BUILT.
Kullanıcıya açık .emd oluştur/aç/kaydet DISABLED, V1.0 BLOCKED.

## Lisans ve yayın
Yeni NuGet paketi/paid API/mandatory cloud/SDK/account yok.
Sadece .NET BCL + mevcut WPF ve Core; özgün 4 Embasic logo/icon
blobları korunmuştur. Tam NOTICE/SBOM/Windows signed installer ve
gerçek UYAP onayı olmadan main merge/publish yok.
Mühendislik planlama tahmini ~33% (ölçülmüş kabul oranı değil);
koşullu 2027 Q2-Q3 planlaması teslim taahhüdü değildir.
