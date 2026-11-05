# Tur 12 — Araştırma kararlarının güvenli native entegrasyonu (06.11.2026)

Kaynak: Embasic E-Note proje talimatları, Google Drive Kaynak ENOTE-01/03/04/05/09,
Tur 09/Tur 11 raporları ve bu projede daha önce aktarılan Araştırma HTML v1.8
prototip kararları. Araştırma sohbetinin bire bir tam dökümü bu turda elde edilmedi;
HTML prototip sonuçları native Windows kabulü olarak kullanılamaz.

## Tur 10 / 11 ile çakışma önlendi
- PR #10 (Tur 10): RichTextBox tabanlı, çevrimdışı, kopya kaydeden
  sınırlı RTF görsel düzenleme. Karmaşık RTF sadakati kabul edilmedi.
- PR #11 (Tur 11): DOCX/ODT yerleşik ZIP+XML üzerinden basit tek
  run'lı paragraf okuma/yeni dosyaya kaydetme; tablolar, çoklu biçimleme,
  resimler, imzalar vb. read-only/fail-closed. Tam Word/LibreOffice
  uyumluluğu iddia edilmiyor.
- Tur 12 doğrulanmış **PR #13** Tur 11 HEAD `849887b5f27088b2e836af687780f7aacecca339`
  üzerine istiflenmiştir; #12 yalnız paralel/uygunsuz base'deki
  bağımsız önizleme denemesidir ve kapatılıp #13'e yönlendirilir.
- RTF ve DOCX/ODT WPF düğmeleri, kısayolları ve lazy workspace
  dosya ağacı kaynakları aynen korunmuştur.

## Yeni somut ürün değerleri
- Core `MarkdownPreviewParser`: başlık 1-6, paragraf, temel tablolama,
  kod fences, yatay çizgi, sıralı/sırasız liste, alıntı. HTML ve
  scriptler **literal metin**; hyperlink tıklama, uzak görsel yükleme,
  dosya erişimi, JavaScript yürütme, tarayıcı motoru **yok**.
- App WPF `MarkdownPreviewDocumentFactory`: yalnız bu literal blokları
  `FlowDocument` içinde gösterir. `Yaz / Önizle / Bölünmüş` modları,
  ~300 ms debounce, kaydedilen kaynak .md içeriğine müdahale etmeyen
  görsel düzen. Bu **salt okunur görsel önizlemedir**, düzenlenebilir
  tam Word/UYAP WYSIWYG değildir.
- Önizleme 200.000 karakter / 3.000 satırda GUI bellek/CPU korunması
  için kısaltılır, **ürünün not boyutuna veya klasör derinliğine sınır
  konulmaz**, kaynak metin diskte veya editörde kısaltılmaz.
- A4 varsayılan sayfa düzeni ve not başına `.md.enote-layout.json`
  Tur 09 altyapısı aynen korunmuştur; gerçek sayfa sonlandırma,
  yazdırma ve PDF henüz yok.
- İki özgün Embasic açık/koyu PNG ve kullanıcıya ait yeni ICO/512 PNG
  asset blobları değiştirilmedi.

## Açık kabul kapıları
- Gerçek Windows WPF GUI/DPI, erişilebilirlik, performans ve çoklu
  belge UX: NOT RUN. Actions WPF BUILD, görsel GUI kabulü değildir.
- Markdown ortak belge modeli ve gerçek WYSIWYG çift yönlü kayıpsız
  düzenleme, Office-free tam DOCX/ODT/RTF düzeni: BLOCKED.
- Windows yazıcı, baskı önizlemesi, PDF ve özellikle gerçek UYAP Editor
  UDF yazarının aç/kaydet/karşılaştırma testi: NOT RUN.
- EMD1/2/ELK1/ELV1/ELH1/ERB1 şifreleme/yedekleme halen deneysel;
  WPF New Secure Note (.emd) DISABLED. Reklam sadece sol panel altı,
  şifreleme ve not içeriği izolasyonu bozulmamalı.
