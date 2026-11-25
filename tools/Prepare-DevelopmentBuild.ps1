# Framework-dependent diagnostic CI output, never an installer or V1 release.
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '../src/Embasic.ENote.App/bin/Release/net10.0-windows'
$output = Join-Path $PSScriptRoot '../artifacts/enote-windows-development'
New-Item -ItemType Directory -Path $output | Out-Null
Get-ChildItem $source -File | Where-Object { $_.Extension -in @('.exe','.dll') -or $_.Name -match '\.(deps|runtimeconfig)\.json$' } | Copy-Item -Destination $output
Copy-Item (Join-Path $PSScriptRoot '../LICENSE') (Join-Path $output 'LICENSE.txt')
Copy-Item (Join-Path $PSScriptRoot '../third-party/Konscious/LICENSE') (Join-Path $output 'Konscious-LICENSE.txt')
@'
EMBASIC E-NOTE — GELİŞTİRME DERLEMESİ

Kurulum programı veya V1.0 sürümü değildir. Windows ve önceden kurulu
.NET 10 Desktop Runtime gerekir. Bu klasör hiçbir runtime/SDK kurmaz.
Embasic.ENote.App.exe ile çalıştırılır. Önce sentetik/önemsiz .md dosyası kullanın.

Yeni: Bul/Değiştir, geri alma, Markdown araçları ve yeni MD/TXT/HTML/RTF/DOCX/ODT
kopyaları. Kaynak/mevcut hedef üzerine dışa aktarma yapılmaz. Görseller ve
bağlantılar metin olarak kalır; karmaşık belge sadakati tamamlanmadı.

TUR56: Sol ağaçtan .md notu/klasörü adlandırma (F2), çalışma alanı içinde taşıma,
silinenlere taşıma ve son silmeyi geri alma. Açık metin ve eski sayfa yan dosyaları
korunur. Silinen not sekmesi salt okunurdur; kaydetmeden önce silmeyi geri alın.
Tamamlanmış silmeler uygulama yeniden açıldığında da geri alınabilir.
Klasör işlemi yalnız .md ve .md.enote-layout.json içeriğiyle sınırlıdır; .emd,
anahtar veya diğer belge içeren klasör reddedilir. .enote-trash elle silinmemeli.
Not+yan dosya iki OS taşımasıdır; güç kesintisinde atomik işlem garantisi yoktur.
Kesintili yan dosya kaydı hata ile görünür; elle kurtarma gerekebilir.

TUR57: Açık .md notu veya sayfa düzeni dışarıdan değişirse editörde uyarı görünür.
Kaydedilmemiş taslak yeniden yüklenmeden önce ayrı .md ve düzen kopyası olarak
kaydedilir. Silinmiş kaynak için de taslağı ayrı kaydedebilirsiniz.

TUR58: Çalışma alanı seçildikten sonra Ctrl+Shift+F ile diskteki düz Markdown
notlarının dosya adı veya içeriğini arayın. Sonuçtan açık taslağa dönebilirsiniz.

TUR59: Çalışma alanı ağacından bir klasör seçip «Yeni .md…» ile doğrudan
orada bir Markdown notu oluşturun. Yeni not düzenleyicide açılır.

TUR60: Haricî dosya ve klasör oluşturma, ad değiştirme ve silme işlemleri
çalışma alanı ağacına yansır. Açık taslaklar kendi sekmelerinde kalır.

Güvenli .emd ve kilitler KAPALI. Düz .md ve dışa aktarılan belgeler ŞİFRESİZDİR.
Fiziksel Windows/yazıcı/DPI kabulleri NOT RUN; nihai hak/kurulum kabulü açık.
Bu CI çıktısı sürüm/installer/dağıtım hakkı onayı değildir. V1.0 BLOCKED.

Üçüncü taraf: Konscious.Security.Cryptography.Argon2 1.3.1 ve Blake2 1.1.1.
Copyright (c) 2017 Keef Aragon (kaynak atfı)
(c) 2024 Keef Aragon (paket atfı)
Özgün MIT metni Konscious-LICENSE.txt içindedir.
'@ | Set-Content (Join-Path $output 'ONCE-OKU.txt') -Encoding utf8
$manifest = @(Get-ChildItem $output -File | ForEach-Object { @{file=$_.Name;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant();size_bytes=$_.Length} })
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'SHA256.json') -Encoding utf8
