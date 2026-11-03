# TUR 11 — Çevrimdışı DOCX/ODT kontrollü metin motoru

Tarih: 2026-11-03  
Durum: REVIEW; Windows CI kanıtı ayrı doğrulanacaktır. **NİHAİ SÜRÜM DEĞİL.**

## Yetkili kaynaklar ve temel

- Yalnız Public `ftmstr/Embasic-E-Note-Build`.
- PR #10 HEAD: `facad8dd09d5df0ba705bc369bacdd11ef1a40d8`.
- PR #10 Windows CI kanıtları: [41647313138](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41647313138) ve [41647321109](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41647321109).
- Tur11: [taslak PR #11](https://github.com/ftmstr/Embasic-E-Note-Build/pull/11), doğrudan PR #10 dalının üzerine; `main` değiştirilmedi.

## Tasarım — güvenlik ve sınırlı sadakat

`OfflineWordPackageStore` yalnız `.docx` ve `.odt` paketlerini .NET `System.IO.Compression` / `System.Xml.Linq` ile yerel olarak işler. Ücretli SDK, hesabı, ağ çağrısı veya ek NuGet bağımlılığı yoktur. .NET kaynakları MIT lisanslıdır; çalışma zamanı ikili dağıtımlarındaki NOTICE ve lisans gereklilikleri kararlı sürüm öncesi ayrı doğrulanmalıdır.

Bu motor **tam Word/Writer/PDF sayfa motoru değildir**. Kaydedilebilir belgeler, DOCX'te tek metin çalıştırması (`w:r/w:t`) içeren basit paragraflar ve ODT'de düz `text:p/text:h` düğümleriyle sınırlıdır. Satır/paragraph sayısı aynı tutulur; doğrudan paragraf metinleri değiştirilir. DOCX'te mevcut `w:pPr/w:rPr` biçimlendirme düğümleri ve ODT'de paragraf stil öznitelikleri korunur. Diğer ZIP parçalarının **açılmış içerik baytları** kopyalanır. ZIP sıkıştırılmış baytlarının ve tüm ZIP metaverisinin birebir aynı kalacağı taahhüt edilmez.

Karmaşık DOCX tabloları, çizimler, çoklu run, izlenen değişiklikler, gömülü nesneler, başlık/altbilgi, imza, makro; ODT iç içe span, liste, tablo, çizimler, imza ve betikler **salt okunurdur**. Bu belgelerin metni kısmen önizlenebilir; kaynak dosya asla dönüştürülmez veya doğrudan üzerine yazılmaz. WPF arayüzünde açık biçim sadakati uyarısı bulunur.

Her kayıtta farklı yeni dosya adı gerekir. Haricî değişikliğe karşı SHA-256 karşılaştırması, ZIP üye sayısı/boyut/sıkıştırma oranı sınırları, güvensiz ZIP üye yolu ve XML DTD/XXE reddi, çıktıyı yeniden okuyarak onaylama ve geçici dosya temizliği uygulanır. Bu **hostile TOCTOU race-free** ya da güç kesintisinde garantili atomiklik kanıtı değildir.

## Kabul testleri

Tur11 test kümesi basit DOCX/ODT okuma, Unicode metin değişikliği, yeniden okuma, kaynak SHA'nın değişmemesi, Word paragraf/run stilleri ve ODT stil öznitelikleri, diğer ZIP dosyalarının içerik korunması, hedef dosya çakışması, dış değişiklik, paragraf sayısı, DTD, bozuk ZIP/XML, ZIP-slip, yinelenen girdiler, karmaşık DOCX tablo/çizim/çoklu-run, karmaşık ODT span/tablo, imza/makro reddi, yanlış uzantı ve geçici dosya temizliği içerir.

Test projesi: `tests/Embasic.ENote.Tur11.Tests`. CI adımı: `Run Tur11 offline DOCX ODT safe-copy and complex-document refusal tests`.

**Önemli:** Karmaşık belge sadakati testi, yalnız güvenli biçimde salt okunur kalması ve orijinal hash'in değişmemesi açısından yapılır. Birebir sayfa, resim, yerleşim/paginasyon sadakati bu turda doğrulanmış değildir.

## Açık kalite kapıları

- Gerçek Windows etkileşimli GUI, DPI, düşük RAM, erişilebilirlik: **NOT RUN**.
- Gerçek Microsoft Word/LibreOffice'te DOCX/ODT yeniden açma ve görüntü karşılaştırması: **NOT RUN**.
- DOCX/ODT tablolar, resimler, birleşik hücreler ve yorumlar üzerinde **tam düzenleme: BLOCKED**.
- Yerel güvenli `.emd` editörü ve katmanlı kilitlerin WPF bağlantısı: **BLOCKED**.
- P0 yazdırma, PDF/DOCX/RTF/UDF dışa aktarma ve gerçek UYAP Writer kabulü: **BLOCKED**.
- Tüm transitive lisans, SBOM, Windows Release installer, imza ve `main` bütünleştirme: **BLOCKED**.

Sonuç: Yalnız kaynak koruması ve kısıtlı paragraf değişiklikleri için yeni bir teknik geliştirme; nihai sürüm ya da tam belge uyumluluğu iddiası yoktur.
