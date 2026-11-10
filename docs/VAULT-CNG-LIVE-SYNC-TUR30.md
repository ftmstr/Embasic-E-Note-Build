# TUR 30 — CNG hizmet geçişi, canlı eşitleme ve kurtarma politikası

W007 / SEC-EMD-05 ve W012 prototipleri REVIEW. WPF New Secure Note .emd DISABLED. V1.0 BLOCKED.

## W007 / SEC-EMD-05

- Geçici Windows SCM hizmeti adanmış SID ile Microsoft Software KSP içindeki kalıcı, dışa aktarılamaz ve yalnız P-256 imza amaçlı makine anahtarını açar. Dosyaya PKCS#8 kopyası yazılmaz. Onay imzası, dışarıdan seçilen dönem ve imza/rol özetleri ayrı doğrulanır. Rol anahtarları fixture dosya ACL'sindedir. Hizmet SID'si ile yönetim hesabı KSP DACL'de ayrı haklara sahiptir. [Microsoft CNG KSP](https://learn.microsoft.com/en-us/windows/win32/seccertenroll/cng-key-storage-providers) · [KSP erişim tanımlayıcısı](https://learn.microsoft.com/windows/win32/seccng/key-storage-property-identifiers)
- Aynı LocalService hesabında ikinci gerçek SCM hizmeti başka hizmet SID'siyle açılır. Özel ELV1, istemci yapılandırması, KSP imzası ve yeni pipe instance girişimleri denenir. Bağımsız standart istemcide seçilmemiş pin güncellemesi reddedilir. İlk süreç SCM ile kapanır, duruş sırasında doğrulanmış yanıt üretilemez. Yeni süreç eski KSP, eski reader ve eski istemci pinini reddeder. Ayrı yayıncı, tam aday için imzalı commit makbuzu alır.
- İki KSP anahtarı fixture sonunda temizlenir. Üretim KSP yükleme ve politika dağıtımı, süreç belleği/enjeksiyon sınırı, rol anahtarlarının kasada muhafazası, kesinti altında dağıtım orkestrasyonu ve donanım anahtarı ayrı kabul kapılarıdır. KSP yazılım sağlayıcısıdır. Yerel yönetici aynı hizmet SID'sini veya eski yapılandırmayı yeniden devreye alabiliyorsa bu fixture mutlak süreç veya eski anahtar iptali kanıtı vermez.

## W012 — canlı ağaç ve şifreli nesne envanteri

- Ortak yazı kilidi ve tam bağımsız commit makbuzu altında kaynak ve kayıtlı hedef snapshot doğrulanır. Canlı ELK1 yol kümesi ve her dosya özeti başta denetlenir. Her dosya tam ebeveyn veya hedef baytlarından biri olmalıdır. Üçüncü değer, ek/eksik yol veya eşleşmeyen seçim durumunda işlem durur. Markdown ve katalog dışı dosyalar korunur.
- Her ELK1 aynı dizinde şifreli geçici dosyaya yazılır, flush ve özet doğrulamasından sonra tam ebeveyn tekrar kontrol edilir. Yerine koyma dosya bazındadır. Sınırlı bir grup sonrasında yeniden açılış karışık eski/yeni değerleri ilerletebilir. Güç kaybı, düşmanca eşzamanlı NTFS yol değişikliği ve tüm ağaç için atomik commit ayrı deneylerdir. ELV1 bağlı fiziksel kimliğiyle kalır. ELH1, yayın nesnesi ve kayıtlı katalogdan doğrulanır.
- İki ardışık kök rekey deneyinde şifreli ELH1 silinip doğrulanmış yedekten kurtarılır. İkinci kök rekey ilk neslin tam seçili kataloğunu kaynak alır.
- Salt okunur tarama, seçili ataları, saklanan ERJ1 planlarını ve üst düzey yayın nesnelerini inceler. Doğrulanan, eksik ve sınıflandırılamayan yolları ayrı raporlar. Kayıtlı katalog dizinleri genel taramada sınıflandırılmaz. Kök sırrı gerektiren iç doğrulama kayıt/açma/eşitlemede yapılır. Bilinmeyen nesneler silme talimatı değildir. Önceki sınırlı kardeş temizleme sözleşmesi korunur.

## Haricî tanık ve ERA1

- Ayrı bir depoda tutulan ERP1 kayıtları bağımsız P-256 imzasını ve çalışma alanı, revizyon, SHA-256 sabitlemesini doğrular. Haricî seçici tam kaydı işaretler. Tanık zinciri ucu ve mevcut ERA1 onayı kayıtla bağlanır. Eski dosyalar saklansa bile yeni sabitleme kullanıldığında geri çekilmiş onay kullanılamaz. Okuma sırasında sabitleme değişirse sonuç belirsizdir. Test ayrı bir dizin ve haricî seçim arayüzü kullanır. Üretimde seçicinin kendisinin bağımsız monoton dayanıklılığı açık kalır.
- Tanık kullanan kök yaşam döngüsü adaptörü eski sağlayıcı kimliğinde okuma ve CAS'i reddeder. Ham eski sağlayıcı kendi yerel kopyasını değiştirebilir. Seçim CAS etrafında değişirse sonuç belirsizdir, eski sağlayıcı dosyasına yazma gerçekleşmiş olabilir.
- Fiziksel Windows güvenliği, kullanıcılar arası DPAPI, disk dolması, güç kaybı, hostile CAS, reparse TOCTOU, GUI/DPI, yazıcı/PDF ve gerçek UYAP Editörü UDF açma/düzenleme/kaydetme NOT RUN.
