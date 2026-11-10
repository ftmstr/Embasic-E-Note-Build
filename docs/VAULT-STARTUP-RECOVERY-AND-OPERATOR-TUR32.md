# TUR 32 — başlangıç tanıtıcıları, şifreli toparlanma ve operatör pini

W007 / SEC-EMD-05, W012 ve EPS1 REVIEW. WPF güvenli .emd DISABLED. V1.0 BLOCKED.

## W007 / SEC-EMD-05

- İki gerçek SCM LocalService sürecine farklı hizmet SID'leri verilir. Özel deney hizmeti yalnız sentetik bayt kullanır ve başlangıçta bilerek açık süreç, thread ve token DACL'leri kurar. Eş hizmet, daraltmadan önce bu üç tanıtıcıyı açar. Sonrasında yeni açma istekleri reddedilirken eski süreç tanıtıcısı sentetik baytları, eski thread tanıtıcısı TID'yi ve eski token tanıtıcısı tür bilgisini okuyabilir. Thread ve token DACL değişiklikleri deney donanımındadır. Ürün başlangıç penceresi, önceden açılmış tanıtıcı iptali ve tüm thread/token yüzeyi açık. [Windows süreç hakları](https://learn.microsoft.com/en-us/windows/win32/procthread/process-security-and-access-rights) · [Token erişim hakları](https://learn.microsoft.com/en-us/windows/win32/secauthz/access-rights-for-access-token-objects)
- Reader ve publisher sırları ayrı RSA-OAEP SHA-256 zarflarında taşınır. Her alıcının dışarı aktarılamaz ayrı Software KSP makine anahtarı vardır. Standart okuyucu publisher anahtarını, aynı hesapta ayrı SID'li eş hizmet reader alıcı anahtarını açamaz. Hizmet ve seçili alıcılar kendi erişim kurallarından geçer. Fixture yöneticisi imzalı dönem ve dağıtım dizini üzerinde yetkilidir. Hizmet açarken ve istemci pinini seçerken açılan sırrın özeti imzalı dönemle karşılaştırılır. Zarfın açılması tek başına dağıtıcı imzası sayılmaz.
- Yanlış KSP ile başlatma pipe oluşturmadan durur. Yeni pin seçilmeden önce operatör onaylı eski hizmeti yeniden başlatır ve seçili içerik korunur. Yeni hizmette eksik veya bozuk zarf çalışmaz. Doğru zarf ve yeni onaylı pin geldikten sonra ayrı publisher imzalı commit makbuzu alır. Dağıtım deneyinde alıcı zarfları şifrelidir. Yönetici güveni, üretim anahtar kaydı ve SCM STOP 1061 açık.

## W012 — canlı eşitleme ve seçili katalog

- İlk dört ELK1 baytı geçici dosyaya yazılıp flush edildikten sonra gerçek işçi süreç zorla kapatılır. Sonraki çağrı ortak kilit altında seçili nesle, çalışma alanına ve belge yoluna HMAC ile bağlı geçici adı, ELK1 başlangıcını ve dosya boyutunu denetler. Yalnız eşleşen sahipsiz dosyayı toparlar. Yabancı belge yoluna kopyalanan ve eski etiketsiz geçici dosyalar kalır. Windows'ta açık paylaşım tanıtıcısı silme ve canlı değiştirmeyi durdurur.
- Bu toparlanma ortak yazıcı protokolüne bağlıdır. Yetkili dosya sistemi saldırganı, fiziksel güç kaybı, disk dolması, reparse/TOCTOU, başka nesillerin geçici dosyaları ve geniş kapsamlı otomatik temizlik kabulü açık.
- Katalog yedeği seçili doğrulanmış ELH1, ELV1 ve ELK1 içeriğini yönetilen dizinlerin dışına kopyalar. Bilinmeyen nesneler kaynakta kalır ve yedeğe girmez. Tam eksik kayıtlı katalog geri yüklenebilir. Bozuk yedek, mevcut dizinin üstüne yazma ve canlı kasa kimliğinin değiştirilmesi reddedilir.

## EPS1 — yönetim alt sınırı ve anahtar rotasyonu

- EPF1 imzalı haricî asgari pin, EPS1 seçicisinden ve sağlayıcıdan ayrı anahtarla doğrulanır. Gerçek Windows kabulünde operatör dizinini yönetici yazar ve ayrı SCM hizmeti sadece okur. Ayrı yönetim hesabı sınırı aynı Windows makinesindedir. Bağımsız fiziksel saklama, operatör hesabının ele geçirilmesi, aynı diskin geri alınması ve TPM/TBS donanım CAS kabulü açık.
- EPS1 anahtar değişikliği çalışma alanı, yeni revizyon sınırı ve iki anahtar özeti üzerinde önceki ve yeni seçici imzalarını doğrular. Eski tarihçe ilk anahtarla okunur. Yeni sınırda eski anahtar, yanlış çalışma alanı, değiştirilmiş revizyon ve anahtar tekrar kullanımı reddedilir. Rotasyon listesi haricen sağlanır.
- Yönetilen sağlayıcı nesnesi yalnız tanığa bağlı işlem yüzeyini açar. Eski ham sağlayıcı prototipi uyumluluk ve bakım deneyi için kalır. Üretim entegrasyonu ve eski kopyaların iptali açıktır. Anti-rollback, donanım CAS ve atomik anahtar teslimi için kabul kanıtı yoktur.
- Fiziksel Windows güvenliği, fiziksel DPAPI, güç kaybı, düşmanca CAS, WPF GUI/DPI, yazıcı/PDF ve gerçek UYAP UDF NOT RUN.
