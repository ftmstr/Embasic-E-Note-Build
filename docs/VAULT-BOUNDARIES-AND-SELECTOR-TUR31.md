# TUR 31 — süreç sınırı, canlı eşitleme ve EPS1 seçici

W007 / SEC-EMD-05 ve W012 prototipleri REVIEW. WPF New Secure Note .emd DISABLED. V1.0 BLOCKED.

## W007 / SEC-EMD-05

- Adanmış SID'li test hizmeti rol dosyaları ve KSP açılmadan önce süreç nesnesinin DACL'sini kurar. SYSTEM, yönetici ve adanmış hizmet SID'si erişebilir. OWNER RIGHTS ACE'si ortak LocalService sahibinin örtük WRITE_DAC yetkisini sınırlar. Aynı hesapta ayrı SID ile çalışan ikinci süreç, doğrulanmış PID üzerinde CREATE_THREAD, VM_OPERATION, VM_READ, VM_WRITE, DUP_HANDLE ve WRITE_DAC haklarını tek tek ister. Test önceden açılmış handle'ları, thread/token handle'larını, kernel veya ayrıcalıklı yönetici müdahalesini kapsamaz. [Microsoft süreç erişim hakları](https://learn.microsoft.com/en-us/windows/win32/procthread/process-security-and-access-rights)
- Reader ve publisher baytları imzalı uç nokta üstverisinden ayrılır. İkinci reader kimlik bilgisi hizmetin özel dizininde hazırdır. Ayrı standart okuyucu publisher dosyasına, hazırlanan reader dosyasına ve dağıtılmış reader yazımına erişemez. Test yöneticisi dağıtım için sınırlı erişim alır. Rol anahtarları CI fixture dosyalarında kalır. Üretim anahtar kasası ve ayrıcalıklı dağıtım kabulü açıktır.
- İlk süreç yöneticiye özel istekle çıkar. Yanlış imza anahtarlı gerçek SCM süreci pipe açmadan önce durur. Doğru ikinci süreç açıldıktan sonra reader/pin dağıtımındaki kesinti sırasında istemci eski reader ile doğrulanmış sonuç alamaz. Dağıtım devam edince yalnız onaylı yeni pin çalışır ve ayrı yayıncı tam aday için imzalı commit makbuzu alır. SCM STOP kontrol kodu TUR 30'daki 1061 bulgusuyla açıktır.

## W012 — canlı ağaç ve seçili katalog

- İlk ELK1 değişiminden sonra seçimi yeniden okuyan yazıcı kilit içindeyken ayrı süreç zorla sonlandırılır. İşletim sistemi yazıcı kilidini bırakır ve sonraki çağrı doğrulanmış eski/yeni karışımda yalnız kalan dosyayı işler. Test noktası tamamlanmış dosya değişimleri arasındadır. Kopyalama ortasında güç kaybı ve sahipsiz geçici dosya temizliği ayrı kabul kapılarıdır.
- Windows fixture açık okuyucu FILE_SHARE_DELETE vermediğinde yerine koymanın durmasını ve hedef içeriğin korunmasını sınar. Kilit kalkınca ReplaceFile tabanlı değiştirmede açık dosya DACL'si yeniden karşılaştırılır. Bu deney fiziksel NTFS ve junction TOCTOU kabulü kapsamına girmez. [Microsoft ReplaceFileW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew)
- Tam seçili kayıtlı snapshot'ın ELH1, ELV1 ve ELK1 içeriği doğrulanır. İç ad alanındaki bilinmeyen nesneler salt okunur raporlanır. Yabancı katalog dizini veya ERJ1 planı çalışma alanına özel incelemeyi durdurur. Mevcut temizlik yalnız güvenli kardeş aday kapsamındadır.

## ERP1 için bağımsız pin seçimi

- EPS1 seçici, ERP1 dosya dizininden ve sağlayıcıdan ayrı bir çalışma alanı dizininde imzalı, ardışık ve değiştirilemez slotlar tutar. Aktif pinin asgari revizyonu ve tam özeti haricî kaynaktan gerekir. Kayıp asgari pin, eksik kuyruk, farklı aynı-yuva adayı ve değiştirilmiş imza reddedilir. Önceki yerel zincirle birlikte haricî asgari pin de geri alınırsa eski neslin okunabildiği regresyonu korunur.
- Aynı ERP1 okuyucusundan bağlı tek yüzey kök yetkisi ve geçiş işlemlerini korur. Eski ham sağlayıcı örneği kendi kopyasını okuyabilir. Bağlı yüzey eski kimliği ve geri çekilmiş tanığı reddeder. Üretimde ham sağlayıcı nesnesinin süreçler arası dağıtımı ve erişim kontrolü açıktır.
- EPS1 dosya sistemi append/no-replace deneyi donanım CAS veya bağımsız monoton seçici kanıtı vermez. Fiziksel Windows güvenliği, güç kaybı, düşmanca CAS, farklı kullanıcı DPAPI, GUI/DPI, yazıcı/PDF ve gerçek UYAP UDF NOT RUN.
