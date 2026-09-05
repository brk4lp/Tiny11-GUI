# Tiny11 GUI Roadmap

Bu belge, Tiny11 GUI'nin mevcut erken beta durumundan daha güvenilir, test edilebilir ve yayımlanabilir bir sürüme taşınması için önerilen geliştirme sırasını tanımlar.

## Temel ilkeler

- Kullanıcıya sunulan her seçenek gerçekten uygulanmalı veya arayüzden kaldırılmalıdır.
- Bir build yalnızca tüm kritik adımlar başarıyla doğrulandığında başarılı sayılmalıdır.
- Yarım kalmış build'lerden kurtulma yeteneği korunmalıdır.
- Tiny10/Tiny11 gibi kırpılmış sistemlerde çalışma amacı korunmalı; `Get-Volume` gibi eksik olabilen WMI sağlayıcılarına yeniden bağımlılık eklenmemelidir.
- Kullanıcıya ait ISO ve başka programlara ait PowerShell/DISM çalışmaları mümkün olduğunca uygulamanın etki alanı dışında tutulmalıdır.
- Geri döndürülemez veya güvenliği azaltan seçenekler açıkça belirtilmelidir.

## Mevcut durum: sahiplik tabanlı temizleme

Otomatik global PowerShell sonlandırma, bütün bağlı Windows imajlarını `/Discard` ile ayırma ve global `dism /cleanup-wim` davranışları kaldırıldı.

Her build artık kendi run-state kaydını oluşturur. Sonraki çalıştırmada yalnızca PID ve başlangıç zamanı doğrulanan önceki Tiny11 PowerShell process'i, kayıtlı Tiny11 ISO'su ve seçilen scratch kökündeki doğrulanmış `tiny11_*` mount/çalışma dizinleri temizlenir. State sistemi öncesindeki sürümlerden kalan dizinler de yalnızca seçilen scratch kökü içinde hedeflenir.

İleride ayrıca açık kullanıcı onayı gerektiren bir agresif kurtarma modu eklenebilir; bu davranış artık normal build başlangıcında otomatik çalışmayacaktır.

---

## Milestone 1 — Build doğruluğu ve kritik hata yönetimi

**Hedef:** Uygulamanın yanlış edisyon işlemesini, bozuk ISO üretmesini veya eski bir çıktıyı başarı olarak göstermesini engellemek.

### 1.1 ESD edisyon indeksi düzeltmesi

- Seçilen ESD imajı tek imajlı WIM'e aktarıldıktan sonra çalışma indeksini `1` olarak güncelle.
- Kaynak edisyon indeksini log ve metadata amacıyla ayrı sakla.
- WIM kaynaklarında özgün indeks davranışını koru.

**Kabul kriterleri:**

- Çok edisyonlu ESD içinden Home dışındaki bir edisyon seçilip başarıyla mount edilebiliyor.
- Çok edisyonlu WIM seçimi mevcut indeksle çalışmaya devam ediyor.
- Yanlış veya bulunmayan indeks build başlamadan reddediliyor.

### 1.2 Native komut çıkış kodlarını zorunlu doğrulama

Aşağıdaki her kritik komuttan sonra `$LASTEXITCODE` kontrol edilmeli ve başarısızlıkta build durdurulmalıdır:

- DISM ESD → WIM export
- WIM mount
- Feature/capability/driver kaldırma
- Component store cleanup
- Registry hive load/unload
- WIM commit/unmount
- Final WIM export/compression
- `oscdimg`

Ortak bir PowerShell yardımcı fonksiyonu kullanılabilir:

```powershell
function Assert-NativeSuccess([string]$Step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE"
    }
}
```

**Kabul kriterleri:**

- Her kritik hata build sonucunu başarısız yapıyor.
- Log, başarısız adımı ve exit code'u açıkça gösteriyor.
- Bir ara adım başarısız olduktan sonra ISO oluşturma devam etmiyor.

### 1.3 Script seviyesinde `try/catch/finally`

- Üretilen scriptin ana akışını `try/catch/finally` içine al.
- `finally` içinde yalnızca o build için bilinen registry hive, ISO, WIM ve geçici dizinleri temizle.
- Hata yakalandığında PowerShell açıkça `exit 1`, başarıda `exit 0` döndürsün.
- C# tarafında `false` dönen build için de hedefli cleanup çağrılmasını sağla.

**Kabul kriterleri:**

- Mount, registry, commit, compression veya ISO oluşturma hatalarından sonra uygulamaya ait kaynak bağlı kalmıyor.
- Hata sonrasında bilgisayarı yeniden başlatmadan yeni build denenebiliyor.

### 1.4 Çıktı ISO'sunu güvenli üretme

- Doğrudan kullanıcı hedefi yerine aynı diskte benzersiz bir geçici ISO üret.
- `oscdimg` başarısını, dosya varlığını, yeni oluşturulma zamanını ve sıfırdan büyük boyutu doğrula.
- Doğrulama başarılıysa hedef dosyayı atomik olarak değiştir/taşı.
- Eski hedef ISO'nun yalnızca var olmasını başarı göstergesi olarak kullanma.
- Mevcut dosyanın üzerine yazma davranışını açıklaştır.

**Kabul kriterleri:**

- Önceden var olan ISO, başarısız build'i başarılı gösteremiyor.
- Başarısız build mevcut sağlam çıktıyı bozmuyor.

---

## Milestone 2 — Seçeneklerin gerçek davranışla eşleştirilmesi

**Hedef:** Arayüzde seçilen ayarların gerçekte üretilen Windows imajıyla birebir örtüşmesi.

### 2.1 Standard/Core davranışını netleştirme

- Core build'in tam teknik tanımını belirle.
- Dinamik script üreticisinde Standard ve Core için farklı build planları uygula veya Core seçeneğini hazır olana kadar kaldır.
- İki mod arasındaki kaldırılan bileşenleri ve servis edilebilirlik farklarını arayüzde açıkla.

**Kabul kriterleri:**

- Standard ve Core seçenekleri doğrulanabilir şekilde farklı sonuç üretiyor.
- Kullanılmayan `isCoreVersion` parametresi kalmıyor.

### 2.2 OOBE seçeneklerini uygulama

- `SkipPrivacyQuestions`, `SkipNetworkConnection` ve `BypassMSAccount` seçeneklerini geçerli bir `autounattend.xml` veya güvenilir Setup mekanizmasıyla uygula.
- Sadece `bypassnro.cmd` dosyası oluşturup çalıştırılmadan bırakılmasını engelle.
- Kullanıcının özel `autounattend.xml` dosyasıyla yerleşik OOBE seçeneklerinin çakışma politikasını belirle:
  - Özel dosya tamamen öncelikli olabilir, veya
  - Güvenli bir XML merge uygulanabilir.
- Seçilen dosyayı XML olarak parse et ve temel Windows unattended yapısını doğrula.

**Kabul kriterleri:**

- Her OOBE seçeneği sanal makine kurulumunda ayrı ayrı doğrulanıyor.
- Özel XML geçersizse build başlamadan anlaşılır hata veriliyor.
- Özel XML kullanılırken hangi yerleşik seçeneklerin yok sayıldığı kullanıcıya gösteriliyor.

### 2.3 AppX kaldırma politikasını şeffaflaştırma

- Şu anda koşulsuz kaldırılan uygulamaları arayüzde görünür yap.
- En azından `Temel kaldırmalar` listesini görüntüle; tercihen her uygulamayı seçilebilir hale getir.
- Paket eşleşmelerinde mümkün olduğunca tam veya doğrulanmış kimlikler kullan.
- `Copilot` ve `WebExperience` gibi geniş substring eşleşmelerini daralt.
- Kaldırılacak gerçek paketlerin build öncesi özetini göster veya logla.

**Kabul kriterleri:**

- Kullanıcı hiçbir şey kaldırmamayı seçtiğinde gizli bir paket kaldırma yapılmıyor.
- Her kaldırılan paket logda seçenek veya politika kaynağıyla görülebiliyor.

### 2.4 Modern Windows ayarlarını doğrulama

- Defender seçeneğinin modern Windows sürümlerindeki gerçek etkisini araştır ve destek matrisini belirle.
- Etkisiz/eski registry anahtarlarını kaldır veya seçeneği doğru şekilde yeniden adlandır.
- Telemetry, Windows Update, Reserved Storage ve BitLocker politikalarını desteklenen Windows build'lerinde doğrula.
- Güvenliği azaltan seçeneklere uyarı ve tercihen ikinci onay ekle.

**Kabul kriterleri:**

- Arayüz hiçbir seçenek için doğrulanmamış kesin sonuç vaat etmiyor.
- Desteklenmeyen Windows build'lerinde seçenek açıkça devre dışı veya uyarılı oluyor.

---

## Milestone 3 — Preflight ve güvenli kullanıcı akışı

**Hedef:** Saatler süren build'i başlatmadan önce bilinen sorunları tespit etmek.

### 3.1 Build öncesi kontroller

- Yönetici yetkisini doğrula.
- Kaynak ISO'nun varlığını, erişilebilirliğini ve temel Windows ISO yapısını doğrula.
- `install.wim` veya `install.esd` bulunduğunu doğrula.
- Gerçek edisyon listesini okuyamazsa sabit indekslere sessizce dönmek yerine build'i engelle veya açık onay iste.
- Seçilen edisyon indeksinin kaynakta bulunduğunu doğrula.
- ADK/`oscdimg` kullanılabilirliğini build başında kontrol et.
- Kullanılacak DISM sürümünü bul ve kaynak imajla uyumsuzluk ihtimalini bildir.
- Scratch ve output dizinlerinde yazma iznini doğrula.
- Tahmini boş alan ihtiyacını hesapla ve disk alanını kontrol et.
- Scratch, kaynak ISO ve output yollarının tehlikeli biçimde iç içe olmadığını doğrula.

**Kabul kriterleri:**

- Eksik ADK, yetersiz disk, geçersiz ISO ve geçersiz indeks mount işleminden önce bulunuyor.
- Kullanıcıya tek bir toplu preflight raporu gösteriliyor.

### 3.2 Build özeti ve onay ekranı

- Başlatmadan önce seçilen edisyonu, çıktı yolunu ve uygulanacak tüm kaldırmaları göster.
- Geri döndürülemez seçenekleri ayrı bölümde vurgula.
- Minimal preset için Defender/Update/driver/input bileşenleri gibi yüksek etkili seçimleri özellikle belirt.

### 3.3 Preset doğruluğu

- Kullanıcı preset sonrasında herhangi bir seçeneği elle değiştirirse preset durumunu `Özel` olarak değiştir.
- Preset tanımlarını tek bir veri modeline taşı; ViewModel içinde tekrarlanan atamaları azalt.
- Her preset için açık amaç ve taviz açıklaması ekle.

---

## Milestone 4 — DISM ve dil uyumluluğu

**Hedef:** İngilizce dışındaki Windows kurulumlarında sessizce çalışmayan temizlik seçeneklerini düzeltmek.

### 4.1 Yerelleştirilmiş DISM çıktısı

- Konsol metni parse edilen DISM çağrılarında `/English` kullan.
- Mümkün olduğunda metin parse etmek yerine yapılandırılmış API/cmdlet sonucunu kullan.
- Parse sonucu boşsa bunu başarı sayma; açık uyarı veya hata üret.
- Capability ve driver kaldırma sonrası gerçek sonuçları yeniden sorgula.

**Kabul kriterleri:**

- Türkçe ve İngilizce Windows hostlarında aynı capability/driver listesi elde ediliyor.
- Sessiz `catch { }` nedeniyle seçeneklerin görünmez şekilde no-op olması engelleniyor.

### 4.2 DISM sürüm seçimi

- Sistem ve ADK DISM adaylarının tamamını değerlendir; ilk bulunan ADK yolunda koşulsuz `break` etme.
- Seçilen DISM yolunu ve sürümünü logla.
- DISM yolu için Program Files konumlarını ortam değişkenlerinden türet.
- Gerekirse ARM64/x86 host senaryoları için mimari kontrol ekle.

### 4.3 ISO mount tespiti

- WMI bağımlılığı olmadan `DriveInfo` yaklaşımını koru.
- Aynı anda başka bir ISO mount edildiğinde yanlış sürücüyü seçme ihtimalini azaltmak için mount sonucunu cihaz yoluyla ilişkilendir.
- Timeout ve hata mesajlarını ayrıntılandır.

---

## Milestone 5 — Test altyapısı ve CI

**Hedef:** Gerçek ISO üzerinde saatler süren manuel denemelere bağımlılığı azaltmak.

### 5.1 Kod seviyesinde test edilebilirlik

- Script üretimini process çalıştırmadan bağımsız saf bir sınıfa taşı.
- `BuildPlan`, `ScriptGenerator`, `ProcessRunner`, `MountManager` ve `CleanupManager` sorumluluklarını ayır.
- Dosya sistemi, process ve saat/GUID üretimini arayüzler üzerinden enjekte et.
- `async void` komutları test edilebilir `Task` tabanlı komut yapısına dönüştür.

### 5.2 Birim ve snapshot testleri

- Her seçenek ve preset için üretilen script snapshot testleri.
- ESD → WIM indeks dönüşümü testi.
- Boşluk, Unicode ve tek tırnak içeren yol testleri.
- Exit code propagation testleri.
- Eski output ISO false-positive testi.
- OOBE XML üretme/merge/validation testleri.
- İngilizce/Türkçe localization anahtar eşitliği testi.
- Paket eşleşmelerinin beklenmeyen uygulamaları kapsamadığını doğrulayan testler.

### 5.3 Entegrasyon testleri

- Küçük test WIM'i veya mock process runner kullanarak mount/commit hata senaryoları.
- İptal sırasında process ağacı ve kaynak temizliği.
- Crash sonrası recovery.
- İki uygulama örneği arasındaki yarış durumu.
- Hyper-V sanal makinede üretilen ISO'nun boot ve OOBE smoke testi.

### 5.4 CI

- Windows runner üzerinde restore, build ve test.
- Nullable ve compiler warning'lerini hata olarak değerlendirme.
- Release artifact üretme ve checksum yayınlama.
- Mümkünse script analizi için PSScriptAnalyzer çalıştırma.

---

## Milestone 6 — Mimari sadeleştirme

**Hedef:** Büyük sınıfları ayrıştırmak ve yeni özellik eklerken regresyon riskini azaltmak.

Önerilen yapı:

```text
src/
  Build/
    BuildPlan.cs
    BuildPreflightService.cs
    Tiny11ScriptGenerator.cs
    BuildOrchestrator.cs
  Imaging/
    DismLocator.cs
    ImageMountManager.cs
    IsoMountManager.cs
    EditionReader.cs
  Processes/
    ProcessRunner.cs
    ProcessResult.cs
  Recovery/
    BuildStateStore.cs
    CleanupManager.cs
  ViewModels/
    MainViewModel.cs
  Models/
    ComponentRemovalOptions.cs
    PresetDefinition.cs
```

Ek çalışmalar:

- Kullanılmayan `AppSettings`, eski script çalıştırma yolu ve preview metotlarını değerlendir; kullanılmayacaklarsa kaldır.
- Namespace adlandırmasını tek biçime getir (`tiny11_ui` veya `Tiny11UI`).
- Localization nesnelerini her property erişiminde yeniden oluşturmak yerine kalıcı örnekler olarak tut.
- Event aboneliklerini yaşam döngüsü sonunda kaldır.

---

## Milestone 7 — Sahiplik tabanlı cleanup ve kurtarma modu

**Hedef:** Mevcut agresif kurtarma kabiliyetini kaybetmeden başka programların çalışmalarına müdahaleyi azaltmak.

> Temel sahiplik kaydı, PID/başlangıç zamanı doğrulaması ve scratch-kökü sınırlandırması uygulanmıştır. Açık kullanıcı onaylı agresif kurtarma ekranı ve tek-instance koruması kalan çalışmalardır.

### 7.1 Build state kaydı

Her build için scratch dizininde bir state dosyası oluştur:

```json
{
  "schemaVersion": 1,
  "runId": "guid",
  "ownerProcessId": 1234,
  "ownerProcessStartTimeUtc": "2026-09-06T10:00:00Z",
  "powerShellProcessId": 5678,
  "isoPath": "...",
  "workDirectory": "...",
  "mountDirectories": ["..."],
  "isoDirectory": "...",
  "status": "running"
}
```

- Başarılı build sonunda state kaydını tamamla veya sil.
- Uygulama açılışında yalnızca yarım kalmış state kayıtlarını kurtar.
- PID yeniden kullanımı riskine karşı PID ile birlikte process başlangıç zamanını doğrula.

### 7.2 Hedefli cleanup

- Sadece kayıtlı process ağacını sonlandır.
- Sadece kayıtlı mount dizinlerini `/Discard` et.
- Sadece seçilen scratch kökü altında doğrulanmış `tiny11_*` dizinlerini sil.
- Path canonicalization yap ve hedefin scratch kökü içinde kaldığını doğrula.

### 7.3 İsteğe bağlı agresif kurtarma

- Mevcut global davranışı `Gelişmiş Kurtarma` butonu veya ayarı olarak koru.
- Çalıştırmadan önce güçlü bir uyarı ve açık kullanıcı onayı göster.
- Önce etkilenecek PowerShell süreçleri ve bağlı imajları listele.
- Mümkünse kullanıcıya yalnızca Tiny11 ile ilişkili öğeleri seçme imkânı ver.

### 7.4 Tek örnek koruması

- Named mutex ile aynı anda iki Tiny11 GUI örneğinin aynı scratch alanında çalışmasını engelle.
- Alternatif olarak her örneğe tamamen bağımsız run root ver.

**Kabul kriterleri:**

- Crash sonrası eski Tiny11 mount'u otomatik temizlenebiliyor.
- Normal cleanup başka PowerShell süreçlerini veya başka DISM mount'larını etkilemiyor.
- Global cleanup hâlâ açık kullanıcı tercihiyle kullanılabiliyor.

---

## Milestone 8 — Performans, kullanılabilirlik ve erişilebilirlik

### 8.1 Log altyapısı

- `LogOutput +=` ile bütün string'i sürekli yeniden oluşturmak yerine sınırlı bir log buffer kullan.
- UI'ya satırları batch halinde aktar; her satırda senkron `Dispatcher.Invoke` kullanma.
- Log görüntüsünde maksimum satır/boyut sınırı belirle.
- Tam logu isteğe bağlı dosyaya yaz.
- DISM ve `oscdimg` ilerlemesini gerçek yüzdeye dönüştür.
- Hassas veya gereksiz tam kullanıcı yollarını paylaşılabilir loglarda maskeleme seçeneği sun.

### 8.2 Pencere ve ölçekleme

- Sabit 1040×920 ve `NoResize` kısıtını kaldır.
- Minimum boyut belirle, pencerenin büyütülmesine izin ver.
- %125–%200 DPI ve 1366×768 ekranlarda test et.
- Klavye navigasyonu, focus sırası ve erişilebilir adlar ekle.

### 8.3 Durum ve ilerleme

- Build adımlarını yapılandırılmış enum/model üzerinden takip et; İngilizce log metni arayarak durum belirleme.
- `Hazırlanıyor → Mount → Özelleştirme → Commit → Sıkıştırma → ISO → Doğrulama` adımlarını göster.
- İptalin güvenli olduğu ve commit sırasında gecikebileceği durumları kullanıcıya bildir.

### 8.4 Dil servisi

- `_currentLanguage` ve `CurrentLanguage` için tek bir kaynak kullan. **Tamamlandı.**
- İngilizce, Türkçe, Rusça, Japonca, Almanca, Fransızca, İspanyolca ve Basitleştirilmiş Çince arayüz kaynaklarını sun. **Tamamlandı.**
- Eksik çeviri anahtarlarında İngilizce fallback kullan. **Tamamlandı.**
- Dil tercihini kalıcı ayarlara kaydet.
- Hard-coded Türkçe/İngilizce durum metinlerini resource dosyalarına taşı.
- Eksik localization anahtarlarını CI testinde hata yap.

---

## Milestone 9 — Yayınlama ve proje sağlığı

### 9.1 Dokümantasyon

- Desteklenen Windows host ve kaynak imaj build matrisini yaz.
- Her kaldırma seçeneğinin sonuçlarını ve geri dönüşsüz etkilerini belgeleyin.
- ADK kurulumu ve `oscdimg` gereksinimini netleştirin.
- Bilinen sınırlamalar ve recovery yönergeleri ekleyin.
- Agresif cleanup davranışını, değiştirilene kadar açıkça belgeleyin.

### 9.2 Dağıtım

- Framework-dependent ve self-contained `win-x64` paket seçeneklerini değerlendirin.
- Uygulamayı code signing ile imzalama planı oluşturun.
- Release checksum ve sürüm notları yayınlayın.
- Uygulama sürümü, commit ve build tarihini tanılama ekranında gösterin.

### 9.3 Lisans ve upstream uyumu

- Proje için açık bir lisans kararı verin.
- Upstream tiny11builder ile alınan kod/fikirlerin lisans durumunu doğrulayın.
- README'deki “all rights reserved” sonucunun katkı ve dağıtım hedefleriyle uyumunu değerlendirin.

---

## Önerilen sürümleme

| Sürüm | Kapsam | Yayın niteliği |
|---|---|---|
| `v1.1.2` | ESD indeks düzeltmesi, exit code kontrolleri, stale output kontrolü | Kritik düzeltme |
| `v1.2.0` | Script `try/finally`, preflight, seçenek doğruluğu | Daha güvenilir beta |
| `v1.3.0` | DISM dil uyumluluğu, test altyapısı, CI | Genel beta |
| `v1.4.0` | Mimari ayrıştırma, log/UI performansı | Release candidate |
| `v2.0.0` | Sahiplik tabanlı cleanup ve açık agresif recovery modu | Kararlı ana sürüm |

## İlk uygulanacak çalışma paketi

İlk PR veya geliştirme dalı yalnızca şu işleri içermelidir:

1. ESD dönüşümünden sonra indeksin `1` yapılması.
2. DISM export, mount, commit ve `oscdimg` exit code kontrolleri.
3. Eski output ISO'nun yanlış başarı üretmesinin engellenmesi.
4. Üretilen scriptin `try/finally` cleanup iskeletine alınması.
5. Bu dört davranış için test altyapısının başlangıcı.

Bu paket, sahiplik tabanlı cleanup temelinin üzerine en yüksek sayıdaki gerçek build hatasını azaltır.
