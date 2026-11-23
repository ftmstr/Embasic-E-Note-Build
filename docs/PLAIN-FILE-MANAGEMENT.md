# Düz Markdown dosya yönetimi — APP-056

23.11.2026. Ürün dilimi: sol ağaçta `.md` notu veya düz Markdown klasörü seç, adlandır/taşı/silinenlere taşı; son silmeyi geri al. Kök çalışma alanı değiştirilemez. Not uzantısı `.md` kalır. Mevcut dosya/klasör ve hedefteki eski sayfa yan dosyası üzerine yazılmaz.

## Açık notlar

Dosya yolu değişirken aynı sekme nesnesi, metin tamponu, kaydedilmiş sürüm özeti, sayfa düzeni ve TextBox geri alma geçmişi korunur. Taşımak otomatik kaydetmek değildir. Klasör hareketi bütün açık alt notların yollarını günceller. Açık kaynak veya sayfa yan dosyası dışarıdan değişmişse işlem reddedilir. İş sırasında diğer uygulama işlemleri geçici olarak kapatılır; disk taraması UI iş parçacığında yapılmaz.

Silme, açık sekmeyi kapatmaz. Kaydedilmemiş metin bellekte tutulur; sekme silindi olarak işaretlenir ve salt okunur olur. Kaydetme/dışa aktarma, silmeyi geri alana kadar engellenir. Uygulamadan çıkışta mevcut kaydedilmemiş değişiklik uyarısı geçerlidir. Bu tampon oturumlar arası otomatik yedek değildir; kullanıcı metni kaydetmeden kapatmayı seçerse kaydedilmemiş değişiklikler saklanmaz.

## Silinenler ve sınırlar

Başarılı silmeler çalışma alanının `.enote-trash/<id>/` klasörüne, özgün göreli yol ve sürümlü `receipt.json` ile taşınır. Dosya kalıcı silinmez. Son silme aynı veya sonraki uygulama oturumunda geri alınabilir. Mevcut özgün hedef, farklı açık sekme veya eksik üst klasör varsa geri alma reddedilir; hem mevcut hedef hem silinen içerik korunur. Sahipsiz kullanıcı `.enote-trash` klasörü uygulama deposu olarak benimsenmez.

İlk kapsam yalnız `.md` dosyaları ve eski `.md.enote-layout.json` yan dosyaları içeren klasörlerdir. Başka belgeler, şifreli `.emd`, anahtar/yedek dosyaları veya reparse point içeren klasörün tamamı ilk değişiklikten önce reddedilir. RTF/DOCX/ODT pencereleri ve güvenli kasa yol bağları değiştirilmez. Karışık belge klasörleri, kopyalama, sürükle-bırak, dosya izleyicisi ve klasör ikonları bu dilimin DONE kapsamı değildir.

Klasör taşıması aynı dosya sisteminde OS rename kullanır; farklı volume için kopyala/sil geri dönüşü yoktur. Tek not ve sayfa yan dosyası iki ayrı OS taşımasıdır. Yan dosya önce taşınır; not taşıması normal hata verirse yan dosya, üzerine yazmadan geri alınır. Güç kesintisinde veya saldırgan haricî yarışta tüm işlem atomik değildir. Tamamlanmamış yalnız-yan-dosya silme kaydı sessiz atlanmaz; dosya yollarını içeren kurtarma hatası verilir. Kalıcı silme/otomatik çöp temizleme yoktur. `.enote-trash` elle kaldırılmamalıdır.

## Kabul

APP-056-01 not/klasör yol değişimi ve açık sekme tutarlılığı; APP-056-02 geri alınabilir silme ve oturum sonrası geri yükleme; APP-056-03 hedef/eski yan dosya/haricî değişiklik/bağlantı koruması. Sonuç yalnız exact HEAD Windows CI ve gerçek hosted Windows UI kanıtıyla raporlanır. Fiziksel Windows/yazıcı/dört kenar/DPI NOT RUN; güvenli `.emd` DISABLED; V1.0 BLOCKED. Yeni runtime/paket/kollektör eklenmez.
