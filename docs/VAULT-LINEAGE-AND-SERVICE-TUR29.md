# TUR 29 — hizmet sınırı, katalog zinciri ve bağımsız kurtarma

Durum REVIEW. W007 / SEC-EMD-05 ve W012 yayın kapıları BLOCKED. WPF New Secure Note .emd DISABLED.

## W007 / SEC-EMD-05

- Geçici Windows hizmeti LocalService hesabında, SCM'nin eklediği ayrı `S-1-5-80-…` hizmet SID'siyle çalışır. Çalışan süreç SID grubunu doğrular. Özel dizin yalnız hizmet SID'si ve SYSTEM için izin verir. İstemci ayrı geçici yerel standart kullanıcıdır. Pipe DACL istemciye okuma/yazma verileri izni verir ve pipe instance üretimini reddeder. Paylaşılan logon hesabının örtük owner DACL değiştirme yetkisi OWNER RIGHTS ile sınırlandırılır.
- SCM nesnesinin korumalı DACL'i SYSTEM ve Administrators yönetimini, standart kullanıcı için yalnız servis durum sorgusunu tanımlar. İstemci `SERVICE_CHANGE_CONFIG` ve `WRITE_DAC` yetkilerini ayrı ayrı ister ve reddi doğrular. Kayıt, çalışma ve söküm yalnız test runner'ında gerçekleşir. [SCM erişim hakları](https://learn.microsoft.com/en-us/windows/win32/services/service-security-and-access-rights)
- İstemci yeni imza SPKI ve reader anahtarı özetini, P-256 ile onaylanmış tam bir ardıl anahtar neslini ve uygulama dışından seçilen nesil numarasını doğrular. Windows fixture taze roller ve imza anahtarıyla ikinci nesli başlatır. İlk anahtarlarla çalışan gerçek bir hizmetten kesintili canlı geçiş, dış seçimin kalıcılığı, KSP/DPAPI muhafazası ve ayrıca ikinci LocalService süreci kabulü ayrı kapılardır.

## W012 — seçili katalog, yedek ve temizlik

- İlk katalog yakalama ve kayıt, aynı korumalı ERJ1 dizinindeki `workspace-write.lock` altında yapılır. Mevcut yedek ancak aynı ELH1 ve tam kaynak doğrulamasıyla yeniden kullanılır. Kayıt, hazırlama, devam, doğrulanmış okuma, ERJ1 ve nesne geri yükleme ile temizlik aynı kilidi kullanır. Test canlı yazıcısı da kilidi alır. Uygulama dışından doğrudan yazan süreçler, ham legacy API'ler ve WPF bu sözleşmeye bağlı değildir.
- Bir rekey commit edildikten sonra kaynak katalog, ERJ1 hedef taahhüdü, seçili manifest ve yayın nesneleri doğrulanarak manifest özetine bağlı tam şifreli snapshot oluşturulur. ELH1, ELV1, etkilenen ve etkilenmeyen ELK1 belgeleri kapsanır. Sonraki rekey tam seçili snapshot ile hazırlanır. Canlı çalışma ağacı kaynakla uyuşmazsa işlem durur. Testte çalışma ağacı ortak kilit altında doğrulanmış şifreli baytlarla eşitlenir. Otomatik canlı ağaç güncellemesi ve çökme sonrası orkestrasyonu ayrı işlerdir.
- ERJ1 bağımsız pending veya son commit makbuzuyla geri yüklenir. ELK1 ve ELH1 nesne geri yüklemesi tam şifreli biçim ve SHA-256 doğrulamasıyla yapılır. ELV1 dosyası yeni Windows dosya kimliğiyle aynı grant'e bağlanamaz ve geri yazma reddedilir.
- Temizlik yalnız bağımsızca seçilmiş bir kardeş ve boş pending yuvası varken, çalışma alanına özel yayın ve günlük dizinlerinde denenir. Seçili ataların ERJ1 dosyaları ve tutulan tüm planlar doğrulanır. Kaybeden kardeşin yalnız başka planların erişmediği ELK1/ELH1 nesneleri kaldırılır. Kasalar, manifestler, snapshot'lar ve yeniden deneme kaydı saklanır. Eksik ata, karışık çalışma alanı ve bozuk günlük temizliği durdurur.

## Sağlayıcı kaybı ve ERA1

- Haricî tanık ardışık P-256 imzalı checkpoint'leri bağımsız sabitlenen zincir ucu ile doğrular. Aynı çalışma alanı, sağlayıcı kimliği devamlılığı, aynı nesilde tam manifest eşitliği, artan boş revizyon ve kimlik tekrarının reddi aranır. Yerel sağlayıcı referans adaptörü, seçilmemiş sağlayıcıda okuma, rezervasyon ve commit'i reddeder. İşlem sırasında haricî seçim değişirse sonuç belirsiz sayılır.
- ERA1 onayı tam nesil, arşiv SHA-256 ve canlı tombstone revizyonuna ek olarak haricî güncel onay seçimine bağlı kullanılabilir. Geri çekilme ve kullanılmış revizyon tekrarında işlem durur. Çıplak eski sağlayıcı ve eski onay API'leri, eski disk kopyaları ve önceden alınmış anahtarlar ayrı tehditlerdir.
- Güven zinciri ucu ile güncel onay seçiminin bağımsız kalıcılığı, üretim dağıtımı ve hostile CAS açık. Donanım CAS, fiziksel güç kaybı, eski kopya iptali ve atomik anahtar teslimi için kanıt yoktur. Fiziksel Windows/DPAPI kullanıcı matrisi, NTFS disk dolması ve reparse TOCTOU, WPF GUI/DPI, yazıcı/PDF ve gerçek UYAP UDF açma–düzenleme–yeniden kaydetme NOT RUN.
