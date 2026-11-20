import AppKit
import CoreGraphics

/// Full-screen selection layer: dimmed screenshot, bright selection, magnifier, size label.
final class CaptureOverlayController {

    enum Mode {
        case region
        case window
    }

    private let screen: NSScreen
    private let screenshot: NSImage
    private let mode: Mode
    private let onFinish: (NSImage) -> Void
    private let onCancel: () -> Void

    private var overlayWindow: OverlayWindow?
    private var overlayView: OverlayView?
    private var escapeMonitor: Any?

    init(screen: NSScreen,
         screenshot: NSImage,
         mode: Mode,
         onFinish: @escaping (NSImage) -> Void,
         onCancel: @escaping () -> Void) {
        self.screen = screen
        self.screenshot = screenshot
        self.mode = mode
        self.onFinish = onFinish
        self.onCancel = onCancel
    }

    func show() {
        let window = OverlayWindow(
            contentRect: screen.frame,
            styleMask: [.borderless],
            backing: .buffered,
            defer: false
        )
        window.setFrame(screen.frame, display: true)
        window.level = NSWindow.Level(rawValue: Int(CGShieldingWindowLevel()) + 1)
        window.backgroundColor = .clear
        window.isOpaque = false
        window.hasShadow = false
        window.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]

        let view = OverlayView(
            frame: NSRect(origin: .zero, size: screen.frame.size),
            screen: screen,
            image: screenshot,
            mode: mode
        )
        view.onSelect = { [weak self] rect in self?.finish(rect: rect) }
        view.onCancel = { [weak self] in self?.cancel() }
        view.onScreenPoint = { [weak self] rect in self?.finish(rect: rect) }

        window.contentView = view
        overlayWindow = window
        overlayView = view

        NSApp.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
        window.makeFirstResponder(view)
        NSCursor.crosshair.push()

        // Odak kaybolsa bile Esc iptal etsin: pencereye değil, uygulamaya gelen tuşlara bakılır.
        escapeMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, event.keyCode == 53 else { return event }
            self.cancel()
            return nil
        }
    }

    private func finish(rect: NSRect) {
        guard let cgImage = screenshot.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
            cancel()
            return
        }
        let scale = screen.backingScaleFactor
        let pixelRect = CGRect(
            x: rect.minX * scale,
            y: (screen.frame.height - rect.maxY) * scale,
            width: rect.width * scale,
            height: rect.height * scale
        ).integral

        guard pixelRect.width >= 4, pixelRect.height >= 4,
              let cropped = cgImage.cropping(to: pixelRect) else {
            cancel()
            return
        }

        let size = NSSize(width: pixelRect.width / scale, height: pixelRect.height / scale)
        let image = NSImage(cgImage: cropped, size: size)
        let completion = onFinish
        dismiss()
        DispatchQueue.main.async { completion(image) }
    }

    private func cancel() {
        let completion = onCancel
        dismiss()
        DispatchQueue.main.async { completion() }
    }

    private func dismiss() {
        if let escapeMonitor {
            NSEvent.removeMonitor(escapeMonitor)
            self.escapeMonitor = nil
        }
        NSCursor.pop()
        overlayWindow?.orderOut(nil)
        overlayWindow = nil
        overlayView = nil
    }
}

/// Borderless window that is allowed to take keyboard focus (Esc / Return).
private final class OverlayWindow: NSWindow {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

private final class OverlayView: NSView {

    let screen: NSScreen
    let image: NSImage
    let mode: CaptureOverlayController.Mode

    var onSelect: ((NSRect) -> Void)?
    var onScreenPoint: ((NSRect) -> Void)?
    var onCancel: (() -> Void)?

    private var selectionRect: NSRect?
    private var dragStart: CGPoint?
    private var mousePoint: CGPoint = .zero
    private var hoveredWindowRect: NSRect?
    private var windowRects: [NSRect] = []
    private var tracking: NSTrackingArea?

    init(frame: NSRect, screen: NSScreen, image: NSImage, mode: CaptureOverlayController.Mode) {
        self.screen = screen
        self.image = image
        self.mode = mode
        super.init(frame: frame)
        if mode == .window {
            windowRects = Self.visibleWindowRects(on: screen)
        }
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override var acceptsFirstResponder: Bool { true }
    override var isOpaque: Bool { false }

    override func resetCursorRects() {
        addCursorRect(bounds, cursor: .crosshair)
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(
            rect: bounds,
            options: [.activeAlways, .mouseMoved, .mouseEnteredAndExited],
            owner: self,
            userInfo: nil
        )
        addTrackingArea(area)
        tracking = area
    }

    // MARK: - Drawing

    override func draw(_ dirtyRect: NSRect) {
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current?.imageInterpolation = .high
        image.draw(in: bounds)
        NSGraphicsContext.restoreGraphicsState()

        NSColor.black.withAlphaComponent(0.45).setFill()
        bounds.fill()

        if let brightRect = selectionRect ?? hoveredWindowRect {
            NSGraphicsContext.saveGraphicsState()
            NSBezierPath(rect: brightRect).addClip()
            NSGraphicsContext.current?.imageInterpolation = .high
            image.draw(in: bounds)
            NSGraphicsContext.restoreGraphicsState()

            NSColor.systemBlue.setStroke()
            let border = NSBezierPath(rect: brightRect)
            border.lineWidth = 1.5
            border.stroke()
        }

        if let selectionRect {
            drawSizeLabel(for: selectionRect)
        }
        if mode == .region && selectionRect == nil {
            drawMagnifier()
        }
    }

    private func drawMagnifier() {
        let side: CGFloat = 118
        let zoom: CGFloat = 2.2
        var origin = CGPoint(x: mousePoint.x + 26, y: mousePoint.y - side - 26)
        if origin.x + side > bounds.maxX { origin.x = mousePoint.x - side - 26 }
        if origin.y < bounds.minY + 8 { origin.y = mousePoint.y + 26 }
        let rect = NSRect(x: origin.x, y: origin.y, width: side, height: side)

        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current?.imageInterpolation = .none
        NSBezierPath(ovalIn: rect).addClip()
        let source = NSRect(
            x: mousePoint.x - side / (2 * zoom),
            y: mousePoint.y - side / (2 * zoom),
            width: side / zoom,
            height: side / zoom
        )
        image.draw(in: rect, from: source, operation: .copy, fraction: 1)
        NSGraphicsContext.restoreGraphicsState()

        NSColor.white.withAlphaComponent(0.9).setStroke()
        let ring = NSBezierPath(ovalIn: rect)
        ring.lineWidth = 2
        ring.stroke()

        NSColor.systemRed.withAlphaComponent(0.9).setStroke()
        let hLine = NSBezierPath()
        hLine.move(to: CGPoint(x: rect.minX + 6, y: rect.midY))
        hLine.line(to: CGPoint(x: rect.maxX - 6, y: rect.midY))
        hLine.lineWidth = 1
        hLine.stroke()
        let vLine = NSBezierPath()
        vLine.move(to: CGPoint(x: rect.midX, y: rect.minY + 6))
        vLine.line(to: CGPoint(x: rect.midX, y: rect.maxY - 6))
        vLine.stroke()
    }

    private func drawSizeLabel(for rect: NSRect) {
        let text = "\(Int(rect.width)) × \(Int(rect.height))"
        let attributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.monospacedDigitSystemFont(ofSize: 11, weight: .medium),
            .foregroundColor: NSColor.white
        ]
        let textSize = (text as NSString).size(withAttributes: attributes)
        var origin = CGPoint(x: rect.midX - textSize.width / 2 - 6, y: rect.maxY + 8)
        if origin.y + 22 > bounds.maxY { origin.y = rect.minY - 28 }
        let box = NSRect(x: origin.x, y: origin.y, width: textSize.width + 12, height: 21)

        NSColor.black.withAlphaComponent(0.75).setFill()
        NSBezierPath(roundedRect: box, xRadius: 5, yRadius: 5).fill()
        (text as NSString).draw(
            at: CGPoint(x: box.minX + 6, y: box.minY + 4.5),
            withAttributes: attributes
        )
    }

    // MARK: - Mouse

    override func mouseMoved(with event: NSEvent) {
        mousePoint = convert(event.locationInWindow, from: nil)
        if mode == .window {
            hoveredWindowRect = windowRects.first { $0.contains(mousePoint) }
        }
        needsDisplay = true
    }

    override func mouseDown(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        mousePoint = point

        if mode == .window {
            if let rect = windowRects.first(where: { $0.contains(point) }) {
                onScreenPoint?(rect)
            } else {
                onScreenPoint?(NSRect(origin: .zero, size: bounds.size))
            }
            return
        }

        dragStart = point
        selectionRect = NSRect(origin: point, size: .zero)
        needsDisplay = true
    }

    override func mouseDragged(with event: NSEvent) {
        guard mode == .region else { return }
        let point = convert(event.locationInWindow, from: nil)
        mousePoint = point
        if let start = dragStart {
            selectionRect = CanvasRenderer.rect(from: start, to: point)
        }
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        guard mode == .region else { return }
        guard let rect = selectionRect else { return }
        if rect.width < 4 || rect.height < 4 {
            selectionRect = nil
            needsDisplay = true
            return
        }
        onSelect?(rect)
    }

    // MARK: - Keyboard

    override func keyDown(with event: NSEvent) {
        switch event.keyCode {
        case 53: // Escape
            onCancel?()
        case 36, 76: // Return / Enter
            if let rect = selectionRect, rect.width >= 4, rect.height >= 4 {
                onSelect?(rect)
            }
        default:
            super.keyDown(with: event)
        }
    }

    // MARK: - Window hit testing

    private static func visibleWindowRects(on screen: NSScreen) -> [NSRect] {
        guard let list = CGWindowListCopyWindowInfo(
            [.optionOnScreenOnly, .excludeDesktopElements],
            kCGNullWindowID
        ) as? [[String: Any]] else { return [] }

        let myPID = ProcessInfo.processInfo.processIdentifier
        let mainHeight = NSScreen.screens.first(where: { $0.frame.origin == .zero })?.frame.height
            ?? NSScreen.screens.first?.frame.height
            ?? 0

        func number(_ value: Any?) -> CGFloat {
            (value as? NSNumber)?.doubleValue ?? 0
        }

        var rects: [NSRect] = []
        for info in list {
            guard let bounds = info[kCGWindowBounds as String] as? [String: Any] else { continue }
            if let pid = info[kCGWindowOwnerPID as String] as? NSNumber, pid.int32Value == myPID { continue }
            if let layer = info[kCGWindowLayer as String] as? NSNumber, layer.intValue != 0 { continue }
            if let alpha = info[kCGWindowAlpha as String] as? NSNumber, alpha.doubleValue < 0.05 { continue }

            let width = number(bounds["Width"])
            let height = number(bounds["Height"])
            guard width > 60, height > 60 else { continue }

            let globalX = number(bounds["X"])
            let globalY = number(bounds["Y"])
            let cocoaY = mainHeight - globalY - height
            let rect = NSRect(
                x: globalX - screen.frame.minX,
                y: cocoaY - screen.frame.minY,
                width: width,
                height: height
            )
            if rect.intersects(NSRect(origin: .zero, size: screen.frame.size)) {
                rects.append(rect)
            }
        }
        return rects
    }
}
