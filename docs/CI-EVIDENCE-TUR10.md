# Tur 10 — Yerel RTF düzenleme ve dosya ağacında çoklu belge türleri

**Tarih:** 2026-11-03  
**Durum:** REVIEW (Windows CI sonucu ayrıca doğrulanmalıdır).  
**Hedef:** Office/OpenOffice/LibreOffice kurulumuna veya internete ihtiyaç duymadan, WPF uygulamasında RTF'yi görsel düzenlemek; özgün dosyanın üzerine yazma riskini engellemek.

## Kodlananlar

- `SafeRtfCopyStore`: geçerli RTF sürüm başlığı şartı, kaynak ve hedefin yalnız `.rtf` olması, dosya SHA-256 karşılaştırması, sembolik bağ/junction kontrolü, aynı dizinde rastgele geçici dosya, flush, son adımda `File.Move(..., overwrite:false)`.
- Orijinal RTF **hiçbir durumda değiştirilmez**. Kayıt yalnız kullanıcı tarafından belirlenen yeni bir `.rtf` dosyasına yapılır. Aynı yol veya mevcut hedef reddedilir.
- `RtfEditorWindow`: WPF `RichTextBox` / `TextRange.Load/Save(DataFormats.Rtf)`, kalın, italik, alt çizgi, liste, hizalama, yazı boyutu, geri al/ileri al, Ctrl+S ile **Farklı Kaydet**, kaydedilmemiş değişiklik uyarısı.
- Çalışma alanı bir düzey açıldığında (lazy) `.md`, `.emd`, `.rtf`, DOC/DOCX/ODT/FODT/ODF, PDF, EPUB, XLS/XLSX/ODS, PPT/PPTX/ODP, CSV/TSV, TXT, HTML, XML, JSON, UDF, `.gdoc` uzantıları **adları ve konumlarıyla** listelenir.
- `.md` çift tıklama mevcut düz metin editörünü; `.rtf` çift tıklama yeni görsel editörü açar. `.emd` açma kapalıdır.

## Önemli uyumluluk ayrımı

| Biçim | Dosya ağacında görünme | Yerel içerik düzenleme | Veri güvenliği |
| --- | --- | --- | --- |
| .md | Evet | Mevcut düz metin editörü | SHA değişiklik kontrolü |
| .rtf | Evet | Yeni WPF görsel editörü | **Sadece yeni kopya**, özgün korunur |
| .doc/.docx/.odt/.fodt/.odf | Evet | **Henüz yok** | Kaydetme kapalı |
| PDF/EPUB/XLS/XLSX/PPT/PPTX/CSV/TSV vb. | Evet | Hayır; içerik önizleme henüz yok | Kaydetme kapalı |
| .gdoc | Evet | İnternetsiz gerçek belge içeriği yok | Bulut kısayolu olarak listelenir |
| .emd | Evet, kilit simgesi | **Güvenlik kabulüne kadar kapalı** | Yanlışlıkla plaintext açma yok |

**RTF gerçeklik sınırı:** WPF RichTextBox yerel bir Rich Text editörüdür; Microsoft Word'ün bütün RTF özelliklerini ve 1:1 sayfa yerleşimini garanti etmez. RTF parser'ının kabul ettiği kaynaklar için yeni kopya üretir. Özellikle görsel, iç içe tablo, font, OLE ve uzun belge sadakati gerçek Windows+Office dışı karşılaştırma testine ihtiyaç duyar. **DOCX/ODT tam düzenleme bu turda uygulanmadı.**

## Testler / kanıt

- `Embasic.ENote.Tur10.Tests`: 16 kaynak koruma, çakışma, Unicode, kötü biçim ve geçici dosya temizliği otomatik regresyonu eklendi.
- Windows CI iş akışına bağımsız Tur10 adımı eklendi. **Bu rapor oluşturulurken CI sonuçları henüz kayda geçirilmedi; yalnız kaynak kodu yazmak PASS değildir.**
- Fiziksel Windows etkileşimli GUI, gerçek NTFS ACL/disk dolu/güç kaybı, Word/LibreOffice görsel karşılaştırması: **NOT RUN**.
- GitHub yalnız `ftmstr/Embasic-E-Note-Build` Public üzerinde PR zincirinin yeni Tur10 dalı; `main` değiştirilmedi.
- Ücretli API, hesap, bulut, yeni NuGet bağımlılığı veya özel lisans gerektirmez.

## Nihai yayın kapıları (BLOCKED)

1. Gerçek Windows'ta tam DOCX/ODT/RTF biçim sadakati; tablo, görsel, üstbilgi/altbilgi, sayfa düzeni, kaymasız gidiş-geliş.
2. Yerleşik şifreli `.emd`, bütün bağımsız kilitler, kurtarma ve güvenlik denetimi; WPF kullanıcı arayüzüne entegrasyonu.
3. P0 yazdırma + PDF/DOCX/RTF/UDF/HTML/TXT/MD dışa aktarma ve gerçek UYAP Editor açma, yeniden kaydetme, karşılaştırma.
4. Windows GUI otomasyonu ve gerçek düşük bellek/DPI/erişilebilirlik testleri, SBOM, lisans, SHA-256 ve imzalı/kurulabilir Release.
5. PR #1–#10 zincirini doğrulanmış biçimde bütünleştirip `main` dalında yeni CI, kurulum ve regresyon kanıtı.

**SONUÇ:** Tur 10 RTF geliştirme turudur; tam sürüm veya V1.0 yayınlanmış değildir.
