#!/usr/bin/env swift
// Kaynak görselden (Resources/AppIconSource.png) macOS uygulama ikonu üretir.
// Görselin saydam/beyaz kenarları kırpılır, squircle maskesi uygulanır,
// Resources/AppIcon.iconset üzerinden Resources/AppIcon.icns yazılır.
// Kullanım: swift scripts/make-icon.swift
import AppKit
import ImageIO
import UniformTypeIdentifiers

let cornerRatio: CGFloat = 0.2237  // macOS ikon köşe oranı

func fail(_ message: String) -> Never {
    FileHandle.standardError.write(Data("\(message)\n".utf8))
    exit(1)
}

let root = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
let sourceURL = root.appendingPathComponent("Resources/AppIconSource.png")
let iconsetURL = root.appendingPathComponent("Resources/AppIcon.iconset")
let icnsURL = root.appendingPathComponent("Resources/AppIcon.icns")

guard let sourceImage = NSImage(contentsOf: sourceURL),
      let sourceCG = sourceImage.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
    fail("Kaynak ikon okunamadı: \(sourceURL.path)")
}

/// Beyaz/saydam olmayan piksellerin sınırlarını bulur (üst-sol orijin).
func contentBounds(_ image: CGImage) -> CGRect? {
    let width = image.width
    let height = image.height
    var pixels = [UInt8](repeating: 0, count: width * height * 4)
    guard let context = CGContext(
        data: &pixels,
        width: width,
        height: height,
        bitsPerComponent: 8,
        bytesPerRow: width * 4,
        space: CGColorSpaceCreateDeviceRGB(),
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
    ) else { return nil }

    context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))

    var minX = width, minY = height, maxX = -1, maxY = -1
    for y in 0..<height {
        for x in 0..<width {
            let index = (y * width + x) * 4
            let r = Int(pixels[index]), g = Int(pixels[index + 1])
            let b = Int(pixels[index + 2]), a = Int(pixels[index + 3])
            let isBackground = (r > 246 && g > 246 && b > 246) || a < 16
            if isBackground { continue }
            if x < minX { minX = x }
            if x > maxX { maxX = x }
            if y < minY { minY = y }
            if y > maxY { maxY = y }
        }
    }

    guard maxX > minX, maxY > minY else { return nil }
    return CGRect(x: minX, y: minY, width: maxX - minX + 1, height: maxY - minY + 1)
}

let bounds = contentBounds(sourceCG) ?? CGRect(x: 0, y: 0, width: sourceCG.width, height: sourceCG.height)
guard let cropped = sourceCG.cropping(to: bounds) else { fail("İkon kırpılamadı") }
print("kaynak: \(sourceCG.width)x\(sourceCG.height) → kırpılan: \(Int(bounds.width))x\(Int(bounds.height))")

func renderIcon(pixels: Int) -> CGImage? {
    let size = CGFloat(pixels)
    guard let context = CGContext(
        data: nil,
        width: pixels,
        height: pixels,
        bitsPerComponent: 8,
        bytesPerRow: 0,
        space: CGColorSpaceCreateDeviceRGB(),
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
    ) else { return nil }

    context.clear(CGRect(x: 0, y: 0, width: size, height: size))
    context.interpolationQuality = .high
    context.saveGState()
    let radius = size * cornerRatio
    let path = CGPath(
        roundedRect: CGRect(x: 0, y: 0, width: size, height: size),
        cornerWidth: radius,
        cornerHeight: radius,
        transform: nil
    )
    context.addPath(path)
    context.clip()
    context.draw(cropped, in: CGRect(x: 0, y: 0, width: size, height: size))
    context.restoreGState()
    return context.makeImage()
}

// Ana penceredeki rozet için 512 px saydam görsel.
let glyphURL = root.appendingPathComponent("Resources/AppGlyph.png")
if let glyph = renderIcon(pixels: 512),
   let destination = CGImageDestinationCreateWithURL(
       glyphURL as CFURL,
       UTType.png.identifier as CFString,
       1,
       nil
   ) {
    CGImageDestinationAddImage(destination, glyph, nil)
    _ = CGImageDestinationFinalize(destination)
}

try? FileManager.default.removeItem(at: iconsetURL)
try FileManager.default.createDirectory(at: iconsetURL, withIntermediateDirectories: true)

let variants: [(name: String, pixels: Int)] = [
    ("icon_16x16", 16), ("icon_16x16@2x", 32),
    ("icon_32x32", 32), ("icon_32x32@2x", 64),
    ("icon_128x128", 128), ("icon_128x128@2x", 256),
    ("icon_256x256", 256), ("icon_256x256@2x", 512),
    ("icon_512x512", 512), ("icon_512x512@2x", 1024)
]

for variant in variants {
    guard let icon = renderIcon(pixels: variant.pixels) else { fail("render başarısız: \(variant.name)") }
    let url = iconsetURL.appendingPathComponent("\(variant.name).png")
    guard let destination = CGImageDestinationCreateWithURL(
        url as CFURL,
        UTType.png.identifier as CFString,
        1,
        nil
    ) else { fail("png yazılamadı: \(variant.name)") }
    CGImageDestinationAddImage(destination, icon, nil)
    guard CGImageDestinationFinalize(destination) else { fail("png tamamlanamadı: \(variant.name)") }
}

let iconutil = Process()
iconutil.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
iconutil.arguments = ["-c", "icns", iconsetURL.path, "-o", icnsURL.path]
try iconutil.run()
iconutil.waitUntilExit()

try? FileManager.default.removeItem(at: iconsetURL)

if iconutil.terminationStatus == 0 {
    print("Hazır: \(icnsURL.path)")
} else {
    fail("iconutil başarısız")
}
