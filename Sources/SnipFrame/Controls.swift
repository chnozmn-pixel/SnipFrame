import AppKit

/// Marka renkleri: logodaki grafit/gümüş palet, açık-koyu görünüme uyarlanır.
enum Brand {

    static func dynamic(light: NSColor, dark: NSColor) -> NSColor {
        NSColor(name: nil) { appearance in
            appearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua ? dark : light
        }
    }

    /// Birincil düğmenin üst tonu: açık görünümde grafit, koyuda gümüş.
    static let fill = dynamic(
        light: NSColor(srgbRed: 0.23, green: 0.23, blue: 0.25, alpha: 1),
        dark: NSColor(srgbRed: 0.90, green: 0.90, blue: 0.92, alpha: 1)
    )
    static let fillEnd = dynamic(
        light: NSColor(srgbRed: 0.11, green: 0.11, blue: 0.13, alpha: 1),
        dark: NSColor(srgbRed: 0.72, green: 0.72, blue: 0.76, alpha: 1)
    )
    /// Dolgu üzerindeki yazı/simge rengi.
    static let onFill = dynamic(
        light: .white,
        dark: NSColor(srgbRed: 0.07, green: 0.07, blue: 0.09, alpha: 1)
    )
    /// Vurgu tonu (hover, seçim, kayıt durumu).
    static let tint = dynamic(light: NSColor.black, dark: NSColor.white)

    static var gradient: NSGradient {
        NSGradient(starting: fill, ending: fillEnd) ?? NSGradient(colors: [fill])!
    }
}

extension NSImage {
    /// Şablon simgeyi tek renge boyar.
    func tinted(_ color: NSColor) -> NSImage {
        let output = NSImage(size: size)
        output.lockFocus()
        draw(in: NSRect(origin: .zero, size: size))
        color.set()
        NSRect(origin: .zero, size: size).fill(using: .sourceAtop)
        output.unlockFocus()
        return output
    }
}

fileprivate func drawCenteredText(_ text: String, font: NSFont, color: NSColor, in rect: NSRect) {
    let style = NSMutableParagraphStyle()
    style.alignment = .center
    style.lineBreakMode = .byTruncatingTail
    let attributes: [NSAttributedString.Key: Any] = [
        .font: font,
        .foregroundColor: color,
        .paragraphStyle: style
    ]
    let size = (text as NSString).size(withAttributes: attributes)
    (text as NSString).draw(
        in: NSRect(x: rect.minX, y: rect.midY - size.height / 2, width: rect.width, height: size.height),
        withAttributes: attributes
    )
}

/// Uygulama logosu (Resources/AppGlyph.png), yoksa gradyan yedek.
final class AppGlyphView: NSView {

    override func draw(_ dirtyRect: NSRect) {
        if let logo = NSImage(named: "AppGlyph") {
            logo.draw(in: bounds, from: .zero, operation: .sourceOver, fraction: 1)
            return
        }

        let rect = bounds.insetBy(dx: 0.5, dy: 0.5)
        let radius = rect.width * 0.235
        let path = NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius)
        Brand.gradient.draw(in: path, angle: -60)

        NSGraphicsContext.saveGraphicsState()
        path.addClip()
        NSColor.white.withAlphaComponent(0.18).setFill()
        NSRect(x: 0, y: rect.height * 0.60, width: rect.width, height: rect.height * 0.40).fill()
        NSGraphicsContext.restoreGraphicsState()

        guard let symbol = NSImage(systemSymbolName: "viewfinder", accessibilityDescription: nil),
              let configured = symbol.withSymbolConfiguration(
                NSImage.SymbolConfiguration(pointSize: rect.width * 0.46, weight: .semibold)
              ) else { return }

        let image = configured.tinted(.white)
        let size = image.size
        image.draw(in: NSRect(
            x: rect.midX - size.width / 2,
            y: rect.midY - size.height / 2,
            width: size.width,
            height: size.height
        ))
    }
}

/// Ana eylem düğmesi: gradyan dolgu, beyaz başlık, sağda kısayol rozeti.
final class PrimaryActionButton: NSButton {

    var hintText = ""
    var leadingSymbol = "viewfinder"

    private var isHovering = false { didSet { needsDisplay = true } }
    private var isPressed = false { didSet { needsDisplay = true } }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        configure()
    }

    required init?(coder: NSCoder) { nil }

    private func configure() {
        isBordered = false
        bezelStyle = .regularSquare
        setButtonType(.momentaryPushIn)
        focusRingType = .none
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(
            rect: bounds,
            options: [.mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect],
            owner: self,
            userInfo: nil
        ))
    }

    override func resetCursorRects() {
        addCursorRect(bounds, cursor: .pointingHand)
    }

    override func mouseEntered(with event: NSEvent) { isHovering = true }
    override func mouseExited(with event: NSEvent) { isHovering = false }
    override func highlight(_ flag: Bool) { isPressed = flag }

    override func draw(_ dirtyRect: NSRect) {
        let rect = bounds.insetBy(dx: 0.5, dy: 0.5)
        let path = NSBezierPath(roundedRect: rect, xRadius: 12, yRadius: 12)

        let top = Brand.fill.blended(withFraction: isHovering ? 0.16 : 0.06, of: .white) ?? Brand.fill
        let bottom = Brand.fillEnd
        NSGradient(starting: top, ending: bottom)?.draw(in: path, angle: -90)

        if isPressed {
            NSColor.black.withAlphaComponent(0.14).setFill()
            path.fill()
        }

        var x = rect.minX + 18
        if let symbol = NSImage(systemSymbolName: leadingSymbol, accessibilityDescription: nil),
           let configured = symbol.withSymbolConfiguration(
            NSImage.SymbolConfiguration(pointSize: 16, weight: .semibold)
           ) {
            let image = configured.tinted(Brand.onFill)
            let size = image.size
            image.draw(in: NSRect(x: x, y: rect.midY - size.height / 2, width: size.width, height: size.height))
            x += size.width + 9
        }

        let titleAttributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 14, weight: .semibold),
            .foregroundColor: Brand.onFill
        ]
        let titleSize = (title as NSString).size(withAttributes: titleAttributes)
        (title as NSString).draw(
            at: NSPoint(x: x, y: rect.midY - titleSize.height / 2),
            withAttributes: titleAttributes
        )

        guard !hintText.isEmpty else { return }
        let hintAttributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.monospacedSystemFont(ofSize: 11, weight: .medium),
            .foregroundColor: Brand.onFill.withAlphaComponent(0.9)
        ]
        let hintSize = (hintText as NSString).size(withAttributes: hintAttributes)
        let pill = NSRect(
            x: rect.maxX - 14 - (hintSize.width + 16),
            y: rect.midY - 10.5,
            width: hintSize.width + 16,
            height: 21
        )
        Brand.onFill.withAlphaComponent(0.16).setFill()
        NSBezierPath(roundedRect: pill, xRadius: 6.5, yRadius: 6.5).fill()
        (hintText as NSString).draw(
            at: NSPoint(x: pill.midX - hintSize.width / 2, y: pill.midY - hintSize.height / 2),
            withAttributes: hintAttributes
        )
    }
}

/// İkincil yakalama kartı: simge, başlık, açıklama; üzerine gelince vurgulanır.
final class CaptureTile: NSButton {

    var symbolName = "macwindow"
    var caption = ""

    private var isHovering = false { didSet { needsDisplay = true } }
    private var isPressed = false { didSet { needsDisplay = true } }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        configure()
    }

    required init?(coder: NSCoder) { nil }

    private func configure() {
        isBordered = false
        bezelStyle = .regularSquare
        setButtonType(.momentaryPushIn)
        focusRingType = .none
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(
            rect: bounds,
            options: [.mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect],
            owner: self,
            userInfo: nil
        ))
    }

    override func resetCursorRects() {
        addCursorRect(bounds, cursor: .pointingHand)
    }

    override func mouseEntered(with event: NSEvent) { isHovering = true }
    override func mouseExited(with event: NSEvent) { isHovering = false }
    override func highlight(_ flag: Bool) { isPressed = flag }

    override func draw(_ dirtyRect: NSRect) {
        let rect = bounds.insetBy(dx: 0.5, dy: 0.5)
        let path = NSBezierPath(roundedRect: rect, xRadius: 11, yRadius: 11)

        if isPressed {
            Brand.tint.withAlphaComponent(0.16).setFill()
        } else if isHovering {
            Brand.tint.withAlphaComponent(0.08).setFill()
        } else {
            NSColor.labelColor.withAlphaComponent(0.06).setFill()
        }
        path.fill()

        let stroke = isHovering
            ? Brand.tint.withAlphaComponent(0.45)
            : NSColor.separatorColor.withAlphaComponent(0.65)
        stroke.setStroke()
        path.lineWidth = 1
        path.stroke()

        let iconHeight: CGFloat = 22
        let titleFont = NSFont.systemFont(ofSize: 13, weight: .semibold)
        let captionFont = NSFont.systemFont(ofSize: 11, weight: .regular)
        let captionHeight: CGFloat = caption.isEmpty ? 0 : 14
        let total = iconHeight + 7 + 16 + (caption.isEmpty ? 0 : 2 + captionHeight)

        var y = rect.midY - total / 2

        if let symbol = NSImage(systemSymbolName: symbolName, accessibilityDescription: nil),
           let configured = symbol.withSymbolConfiguration(
            NSImage.SymbolConfiguration(pointSize: iconHeight, weight: .regular)
           ) {
            let image = configured.tinted(isHovering ? Brand.tint : .secondaryLabelColor)
            let size = image.size
            let scale = min(1, iconHeight / max(size.height, 1))
            let drawSize = NSSize(width: size.width * scale, height: size.height * scale)
            image.draw(in: NSRect(
                x: rect.midX - drawSize.width / 2,
                y: y + (iconHeight - drawSize.height) / 2,
                width: drawSize.width,
                height: drawSize.height
            ))
        }
        y += iconHeight + 7

        drawCenteredText(
            title,
            font: titleFont,
            color: .labelColor,
            in: NSRect(x: rect.minX + 6, y: y, width: rect.width - 12, height: 16)
        )
        y += 16

        if !caption.isEmpty {
            y += 2
            drawCenteredText(
                caption,
                font: captionFont,
                color: .secondaryLabelColor,
                in: NSRect(x: rect.minX + 6, y: y, width: rect.width - 12, height: captionHeight)
            )
        }
    }
}
