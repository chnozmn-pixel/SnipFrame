# SnipFrame

Windows **Snipping Tool**'ün macOS karşılığı: menü çubuğunda yaşayan, global kısayolla bölge/pencere/tam ekran görüntüsü alan ve aldığı görüntüyü üzerine çizim yaparak panoya kopyalayan ya da PNG olarak kaydeden native bir Swift/AppKit uygulaması.

Depo: <https://github.com/chnozmn-pixel/SnipFrame>


Harici bağımlılık yok; sadece Apple çatısı (AppKit, CoreGraphics, Carbon) kullanır.

## Özellikler

| Özellik | Açıklama |
| --- | --- |
| Bölge yakalama | Ekran karartılır, seçim yaparken büyüteç + piksel boyutu göstergesi çıkar |
| Pencere yakalama | İmlecin altındaki pencere canlı olarak vurgulanır, tıklayınca kırpılır |
| Tam ekran | İmlecin bulunduğu ekranı doğrudan yakalar |
| Gecikme | 0 / 3 / 5 saniye (menü öğesinden) |
| Çoklu ekran | Seçim, imlecin bulunduğu ekranda yapılır; Retina'da native piksel kalitesi |
| Anotasyon | Kalem, fosforlu kalem, ok, dikdörtgen, elips, mozaik (gizleme), metin |
| Renk & kalınlık | Renk seçici + çizgi kalınlığı kaydırıcısı |
| Araç seçimi | Ana pencereden varsayılan anotasyon aracı seçilir; editör o araçla açılır |
| Tema | Logoya uygun nötr grafit/gümüş palet; açık ve koyu görünüme uyarlanır |
| Geri al / yinele | ⌘Z / ⌘⇧Z |
| Çıktı | ⌘C ile panoya kopyala, ⌘S ile PNG kaydet (varsayılan: Masaüstü) |
| Ana pencere | Dock simgesine tıklayınca açılır; gradyan "Yeni Yakalama" düğmesi, Pencere / Tam Ekran kartları, gecikme seçimi |
| Uygulama simgesi | `Resources/AppIconSource.png`'den üretilir; `swift scripts/make-icon.swift` → `AppIcon.icns` + `AppGlyph.png` |
| Arka plan | Menü çubuğunda da yaşamaya devam eder; ana pencere kapansa bile kısayol çalışır |

## Dil desteği

Arayüz **sistem dilini** izler. Paketle gelen diller: İngilizce, Türkçe, Almanca, Fransızca,
İspanyolca (`Resources/<dil>.lproj/Localizable.strings`). Yeni dil eklemek için aynı anahtarlarla
bir `.lproj` klasörü oluşturup `scripts/build-app.sh` ile yeniden paketleyin.

## Kısayol

Varsayılan **⌘⇧X**'tir. Ana penceredeki **Kısayol** alanına tıklayıp yeni kombinasyonu tuşlayarak
değiştirebilir, **Sıfırla** ile varsayılana dönebilirsiniz. Seçim `UserDefaults`'ta saklanır
(`com.local.snipframe`). Kombinasyonda ⌘ / ⌥ / ⌃ bulunmalıdır (fonksiyon tuşları istisna).

## Kurulum

Gereksinim: macOS 13+, Xcode Command Line Tools (Swift 5.9+).

```bash
./scripts/build-app.sh          # build/SnipFrame.app üretir
open build/SnipFrame.app         # uygulamayı başlatır
```

> Uygulamayı `/Applications` klasörüne taşıyabilirsiniz. Derleme sonrası ad-hoc imza
> yenilendiğinde macOS ekran kaydı iznini yeniden isteyebilir; bu normaldir.

### Ekran Kaydı izni

İlk yakalamada macOS izin ister. Vermezseniz veya reddederseniz:

**Sistem Ayarları → Gizlilik ve Güvenlik → Ekran Kaydı → SnipFrame** açılır, ardından
uygulamayı kapatıp yeniden açın.

## Kullanım

- Dock'taki **SnipFrame** simgesine (veya menü çubuğundaki **viewfinder** simgesine) tıklayıp
  **Bölge / Pencere / Tam Ekran** düğmelerinden birini seçin; ana pencereden uygulamaya girmeden
  **⌘⇧X** ile de bölge seçimine girebilirsiniz. Ana pencere kapalıyken Dock simgesine tıklamak onu geri getirir.
- Bölge modunda: sürükleyip bırakın → editör açılır. `Esc` iptal, `Enter` onay.
- Pencere modunda: istediğiniz pencerenin üzerine gelip tıklayın.
- Editörde: araç seçin, çizin; `⌘C` kopyala, `⌘S` kaydet, `⌘Z` geri al, `Esc` kapat.
  Metin aracında tıklayıp yazın, `Enter` ile yerleştirin.

## Proje yapısı

```
Package.swift                       SwiftPM paketi (macOS 13+)
Resources/Info.plist                .app paketi için Info.plist
Resources/AppIconSource.png          logo kaynağı
Resources/AppIcon.icns              uygulama simgesi (betikle üretilir)
Resources/AppGlyph.png              ana pencere rozeti (betikle üretilir)
Resources/*.lproj                   yerelleştirme dosyaları (en, tr, de, fr, es)
scripts/build-app.sh                derleme + .app paketleme + ad-hoc imza
Sources/SnipFrame/
  main.swift                        uygulama girişi (regular policy — Dock simgesi)
  AppDelegate.swift                 ana pencere, menü çubuğu, kısayol, akışlar, izinler
  MainWindowController.swift        ana pencere düzeni (başlık, eylemler, gecikme)
  Controls.swift                    özel çizilmiş düğme/kart bileşenleri + marka renkleri
  Localization.swift                L() yardımcısı (sistem dili)
  Shortcut.swift                    kısayol modeli, tuş adları, UserDefaults
  ShortcutRecorderButton.swift      kısayol kaydetme alanı
  HotkeyManager.swift               Carbon global kısayol
  ScreenCaptureService.swift        ekran yakalama + TCC izin yardımcıları
  CaptureOverlay.swift              karartma, bölge/pencere seçimi, büyüteç
  EditorWindowController.swift      editör penceresi, araç çubuğu, kopyala/kaydet
  EditorCanvasView.swift            tuval, çizim girdisi, metin, undo, export
  CanvasRenderer.swift              anotasyon modeli + ortak çizim motoru
  CanvasContainerView.swift         kaydırma için flipped kapsayıcı
```

## Bilinen sınırlar

- Serbest (lasso) seçim ve kayıt (video) yok — bkz. `PLAN.md` yol haritası.
- Global kısayol sabittir (⌘⇧X); kullanıcı tarafından değiştirilemez.
- Uygulama imzası ad-hoc olduğu için ilk dağıtımda Gatekeeper uyarısı çıkabilir.
