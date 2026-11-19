import AppKit

/// Menü çubuğu (sistem tepsisi) simgesi: logodaki iki çerçeve motifi, template olarak.
enum StatusIcon {

    static func make() -> NSImage {
        let side: CGFloat = 20
        let image = NSImage(size: NSSize(width: side, height: side), flipped: false) { _ in
            NSColor.black.setStroke()

            func frame(_ rect: NSRect) -> NSBezierPath {
                let path = NSBezierPath(roundedRect: rect, xRadius: 2.4, yRadius: 2.4)
                path.lineWidth = 1.6
                return path
            }

            // Sol-alt çerçeve ve sağ-üst çerçeve birbirinin üzerine biner.
            frame(NSRect(x: 2.2, y: 5.6, width: 11.4, height: 11.4)).stroke()
            frame(NSRect(x: 6.4, y: 2.2, width: 11.4, height: 11.4)).stroke()
            return true
        }
        image.isTemplate = true
        image.accessibilityDescription = "SnipFrame"
        return image
    }
}
