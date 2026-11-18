# TUR 46 · Fiziksel yazıcı ve DPI kabulü

Bu kabulü fiziksel Windows oturumunda, A4 tepsisi olan bir yazıcı ve cetvelle
çalıştırın. Sonuçları yazıcıdan çıkan yaprak ve gerçek ekran ölçeği üzerinden
kaydedin. Barındırılan Windows CI, yerel spooler ve PDF sürücüsü ayrı kanıtlardır.

1. Draft PR #47'nin doğrulanacak HEAD SHA'sını ve Windows yapı numarasını not
   edin. Bu committen Release yapılandırmasında `dotnet build src/Embasic.ENote.App
   --configuration Release` çalıştırın ve çıktıdaki E-Note EXE'yi doğrudan açın.
2. `fixtures/physical-print-a4.md` ile aynı adlı `.enote-layout.json` yan
   dosyasını aynı geçici klasöre kopyalayın. Windows PowerShell'de iki dosya için
   `Get-FileHash -Algorithm SHA256` ile başlangıç değerlerini kaydedin.
3. E-Note'ta notu açın ve `Ctrl+P` kullanın. Önizlemenin A4 dikey, 210 × 297 mm ve
   sol, sağ, üst, alt sırasıyla 17, 23, 19, 27 mm gösterdiğini doğrulayın.
   Sayfa sayısını ve tablonun ilk ve son satırını kaydedin.
4. Gerçek ekran ölçeğinde %100 ve %150 için uygulamayı ayrı başlatıp önizlemeyi
   açın. Ölçek seçeneklerinden biri yoksa uygulanabilen iki değerle sonuçları
   etiketleyin. Sayfa sayısı ve geometri aynı kalmalı, Yazdır ve Kapat düğmeleri
   görünür ve erişilebilir olmalı. Birden fazla fiziksel monitör varsa farklı
   ölçekler arasında pencere taşıma deneyini ayrı kaydedin.
5. Aynı önizlemeden Microsoft Print to PDF ile geçici referans PDF kaydedin.
   Yerel fiziksel yazıcıya bir kopya gönderin. Sürücüde A4, tek yüz, gerçek boyut
   (%100) seçin. Kâğıda sığdırma, kenarlıksız büyütme ve otomatik ölçekleme
   kullanmayın. Yazıcı modeli, sürücü ve ölçek ayarını kaydedin.
6. Çıktıda Türkçe metni ve tablonun ilk/son satırını kontrol edin. Kâğıdın dört
   kenarından aynı dış tablo çizgilerine olan uzaklıkları ayrı ayrı mm olarak
   ölçün. PDF ölçüm aracında eşleşen çizgileri ölçüp karşılaştırın. Her kenarda
   fark en fazla 2 mm olmalı. Alt kenar ölçümü son görünür tablo çizgisine aittir,
   doğrudan sayfa kenar boşluğunu ölçmez. Sürücü çıktı alanını kırptıysa bunu
   kabul hatası olarak kaydedin.
7. Önizlemeyi kapatın ve Markdown ile yan dosyanın SHA-256 değerlerini yeniden
   hesaplayın. İlk değerlerle eşleşmeli. Sonuçta cihaz modeli, uygulama HEAD,
   ölçekler, sayfa sayısı, dört PDF ölçümü ve dört kâğıt ölçümünü kaydedin.

Her çalışılan fiziksel kabulü `PASS` veya `FAIL` olarak, erişilemeyen adımı
`NOT RUN` olarak kaydedin. Yalnızca sentetik belge ve çıktıyı saklayın.
