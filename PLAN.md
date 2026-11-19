# SnipFrame — Plan

## Hedef
Windows Snipping Tool'un temel iş akışını macOS'ta birebir karşılamak:
**kısayol → seçim → düzenleme → pano/kayıt**. Harici bağımlılık yok, native AppKit.

## Kapsam (v1 — bu sürümde tamamlandı)

1. **Dock uygulaması + menü çubuğu**: Dock simgesi ve düğmeli ana pencere (Bölge / Pencere /
   Tam Ekran), ayrıca menü çubuğunda kalıcı durum simgesi.
2. **Global kısayol**: varsayılan ⌘⇧X, ana pencereden kullanıcı tarafından değiştirilebilir
   (Carbon `RegisterEventHotKey`, Accessibility izni gerekmez; çakışmada uyarı).
3. **Yakalama modları**
   - Bölge: tam ekran karartma, sürükleyerek seçim, seçim dışı karartılmış / içi parlak,
     büyüteç (2.2×) + canlı `genişlik × yükseklik` etiketi, `Esc` iptal, `Enter` onay.
   - Pencere: `CGWindowListCopyWindowInfo` ile imleç altındaki normal katman penceresi
     vurgulanır, tıklayınca o dikdörtgen kırpılır.
   - Tam ekran: imlecin bulunduğu ekranın tamamı.
   - Gecikme: 0 / 3 / 5 sn (menüde işaretli, `UserDefaults`'ta saklanır).
4. **Ekran yakalama**: `CGDisplayCreateImage` (Retina'da native piksel), çoklu ekran desteği,
   seçim imlecin olduğu ekranda.
5. **İzin akışı**: `CGPreflightScreenCaptureAccess` / `CGRequestScreenCaptureAccess`,
   yönlendirici uyarı + Sistem Ayarları kısayolu.
6. **Editör**: kalem, fosforlu kalem, ok, dikdörtgen, elips, mozaik, metin; renk seçici,
   kalınlık kaydırıcısı; undo/redo; tuval kaydırma + yakınlaştırma.
7. **Çıktı**: ⌘C panoya (flatten edilmiş, pixel-perfect), ⌘S PNG kaydet
   (`Ekran Görüntüsü yyyy-MM-dd saat HH-mm-ss.png`, varsayılan Masaüstü).
8. **Paketleme**: `scripts/build-app.sh` → `build/SnipFrame.app` + ad-hoc imza; uygulama simgesi
   `scripts/make-icon.swift` ile logo kaynağından üretilir.
9. **Yerelleştirme**: sistem diline uyan arayüz; en / tr / de / fr / es.

## Mimari

```
⌘⇧X ──► HotkeyManager ──► AppDelegate.startCapture(mode:)
                               │
                    (gecikme)  ▼
                     ScreenCaptureService.capture(screen)  → NSImage (nokta boyutu, native piksel)
                               │
                               ▼
                    CaptureOverlayController (seçim + büyüteç + kırpma)
                               │  kırpılmış NSImage
                               ▼
                    EditorWindowController ──► EditorCanvasView ──► CanvasRenderer
                               │                                     │
                        kopyala / kaydet ◄────────── flatten ────────┘
```

Temel kararlar:

- **Koordinatlar nokta cinsinden** tutulur (anotasyonlar); export sırasında CTM
  `backingScaleFactor` kadar ölçeklenerek native piksel çıktı alınır. Böylece hem ekranda
  hem PNG'de aynı görünüm korunur.
- **Tek çizim motoru**: `CanvasRenderer` hem ekran çiziminde hem export'ta kullanılır →
  önizleme ile kaydedilen görüntü birebir aynı.
- **Mozaik**, temel görüntüden kırpılan bölgenin küçük bir bitmap'e indirilip
  interpolasyonsuz büyütülmesiyle yapılır (harici filtre yok).
- **Seçim katmanı**, `CGShieldingWindowLevel()` üstünde borderless bir penceredir; canlı
  yakalama görüntüsünü arka plan olarak kullanır, böylece seçim "gerçek zamanlı" görünür.
- **İmza**: ad-hoc. İzin kalıcılığı için bundle kimliği (`com.local.snipframe`) sabittir.

## Doğrulama (manuel test planı)

TCC/ekran kaydı ve global kısayol otomatik test edilemediği için aşağıdakiler elle denenmelidir:

1. `./scripts/build-app.sh && open build/SnipFrame.app` → menü çubuğunda simge görünür.
2. ⌘⇧X → karartma + büyüteç gelir, sürükle-bırak → editör açılır, boyut etiketi doğru.
3. Esc → seçim iptal; seçim < 4 pt ise yakalama yapılmaz.
4. Pencere modu → imleç altındaki pencere vurgulanır, tıklayınca sadece o pencere kırpılır.
5. Tam ekran → editörde görüntü ekranla aynı, Retina'da keskin.
6. Her araç + renk + kalınlık; metin Enter ile yerleşir; ⌘Z/⌘⇧Z.
7. ⌘C → Pages/Preview'e yapıştır (Retina çözünürlük korunmalı).
8. ⌘S → PNG oluşur, dosya adı tarih/saat içerir.
9. Gecikmeli yakalama (3 sn) → menü kapanır, süre sonunda seçim katmanı gelir.
10. İkinci ekran varsa: imleç oradayken yakalama o ekranda yapılır.

## Yol haritası (v2+)

- Serbest (lasso) seçim ve seçim sonrası köşe tutamaçlarıyla düzeltme.
- Kısayol atama arayüzü (çakışma kontrolü ile).
- Ekran kaydı (video) ve GIF çıktısı.
- Pencere gölgesi dahil/hariç seçeneği, "son yakalamayı tekrarla".
- Yakalanan görüntüyü ekranda sabitleme (pin) ve OCR (Vision) ile metin kopyalama.
- Kaydetme klasörü/otomatik kopyalama tercihleri için ayarlar penceresi.
- Evrensel (arm64 + x86_64) dağıtım ve notarization.
