# Embasic E-Note — uygulamayı tamamlama sırası

22.11.2026 kullanıcı yönlendirmesi: denetim kayıtlarını büyütmek yerine çalışan uygulamayı tamamla. V1.0 kabul kapıları korunur; önceki %98 tahmini kullanıcı işlevlerinin tamamlanmasını ölçmüyor ve tekrarlanmayacak.

| Sıra | Teslim edilecek kullanıcı akışı | Kabul |
| --- | --- | --- |
| 1 | Not aç, düzenle, bul/değiştir, geri al; MD/TXT/HTML/RTF/DOCX/ODT yeni kopyası oluştur | Gerçek Windows UI + kaynak/mevcut hedef/yan dosya koruması; gerçek belge okuyucusunda açılış |
| 2 | Günlük dosya yönetimi: not/klasör yeniden adlandırma, taşıma, geri alınabilir silme; açık notların tutarlılığı | Kaynak kaybı olmadan gerçek dosya sistemi ve Windows UI kabulü |
| 3 | Güvenli .emd ve bağımsız kilitlerin günlük kullanıma entegrasyonu | Mevcut güvenlik modelinin açık engelleri kapatılmadan etkinleştirme yok; kurtarma/yanlış parola/bozuk veri/eski yedek kabulü |
| 4 | Son Windows kullanıcı kabulü, yazıcı/dört kenar/DPI ve dışa aktarılan belge karşılaştırması | Fiziksel çalıştırılmayan madde NOT RUN; mevcut otomatik PDF/UYAP/RTF/Word kabulleri korunur |
| 5 | Nihai paket, hak/NOTICE incelemesi ve kurulum | Önceki legal kanıtlar kullanılır; yalnız eksik gerçek paket kapsamı kapatılır. Main merge/installer için mevcut kapılar korunur |

Bu tur ilk kullanıcı akışını kapatır. Dışa aktarma sınırlaması açık gösterilir: temel başlık/biçim/liste/tablo aktarılır; liste işaretleri bazı biçimlerde metin olarak kalır. Görseller ve bağlantılar kaynak metindir; uzak içerik yüklenmez. Karmaşık Word/Writer sadakati ve tüm şifreli not iş akışı tamamlanmış sayılmaz.

Her sonraki tur en az bir somut kullanıcı akışını çalışır hale getirmeli veya gerçek bir kabul engelini kapatmalıdır. Yeni provenance-only tur varsayılan devam talimatı olmayacaktır.

23.11.2026 TUR56: ikinci akış için düz .md notu/klasörü adlandırma, taşıma, kalıcı olmayan silme ve oturum sonrası geri alma eklendi. Açık tampon, eski sayfa yan dosyası, hedef çakışması ve klasör altındaki sekmelerin yol tutarlılığı kabul kapsamıdır. Kapsam/sınırlar: `PLAIN-FILE-MANAGEMENT.md`. Karışık belge klasörleri, kopyalama/sürükle-bırak, dosya izleyicisi ve ikonlar tamamlanmış sayılmaz. Nihai durum exact HEAD CI ve tur raporuyla belirlenir.

24.11.2026 TUR57: seçili açık .md notu ve sayfa yan dosyası için disk değişikliği bildirimi eklendi. Taslak otomatik değiştirilmez. Kaydedilmemiş metin veya düzen varsa yeniden yükleme önce yeni bir .md ve sayfa yan dosyası kopyası ister. Eksik kaynak için ayrı kopya kullanılabilir. Kontrol etkin düzenleyicide üç saniyede bir ve kaydetme öncesi çalışır. Not ve düzen doğrulaması haricî yazarlara karşı atomik işlem sağlamaz. Exact HEAD CI sonucu tur raporunda kaydedilir.

24.11.2026 TUR58: çalışma alanı .md dosyalarında Türkçe büyük/küçük harf duyarsız, Unicode uyumlu ad ve içerik araması. Disk sürümü ve bağlı olmayan alt klasörler taranır. İçerik başına 2 MiB, görüntülemede ilk 200 sonuç sınırı uygulanır. Atlanan erişim ve içerik sınırı görünür. Arama iptal edilebilir. Göreli yol aynı adlı notları ayırır. Sonuç açılırken mevcut not sekmesi ve kaydedilmemiş tampon korunur.

24.11.2026 TUR59: çalışma alanı ağacında seçili klasörden «Yeni .md…» ile boş not oluşturulur ve düzenleyicide açılır. Türkçe ve Unicode adlar korunur. Gerekirse .md uzantısı eklenir. Geçersiz ad, açık sekme, mevcut dosya/klasör, eski düzen dosyası ve bağlantılı veya erişilemeyen klasör reddedilir. Yeni not için düzen dosyası oluşturulmaz.

25.11.2026 TUR60: çalışma alanı dosya ağacı haricî oluşturma, ad değiştirme ve silme olaylarını izler. Olaylar 350 ms aralıklarında birleştirilir ve yalnız daha önce açılmış dallar okunur. Değişmeyen yol ve türlerde mevcut düğüm kullanılır, böylece seçili klasör ve genişletilmiş dallar korunur. Silinen veya adı değişen yol ağaçta güncellenir. Açık not sekmeleri otomatik taşınmaz ve kaydedilmemiş metin korunur. Erişim hatasında son liste korunur, durum satırı uyarır ve üç saniye sonra yeniden okuma denenir. Çalışma alanı değişince eski izleyici kapatılır.

25.11.2026 TUR61: Markdown araç çubuğunda «Ağaçta göster» açık notun üst dallarını açar, ilgili satırı seçer ve görünür alana getirir. İlgili dallar diskten yeniden okunur. Aynı adlı başka not, eksik yol, çalışma alanı dışı yol veya erişim hatası taslağı değiştirmez.
