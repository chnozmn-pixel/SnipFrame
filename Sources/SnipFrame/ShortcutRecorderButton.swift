import AppKit

/// Tıklanınca tuş kombinasyonu dinleyen, mevcut kısayolu rozet olarak gösteren alan.
final class ShortcutRecorderButton: NSButton {

    var onCapture: ((Shortcut) -> Void)?

    private(set) var isRecording = false {
        didSet { needsDisplay = true; window?.invalidateCursorRects(for: self) }
    }

    private var displayText = "" {
        didSet { needsDisplay = true }
    }

    private var isHovering = false { didSet { needsDisplay = true } }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        configure()
    }

    required init?(coder: NSCoder) { nil }

    private func configure() {
        isBordered = false
        bezelStyle = .regularSquare
        setButtonType(.momentaryChange)
        focusRingType = .none
        refusesFirstResponder = false
    }

    func setShortcutText(_ text: String) {
        guard !isRecording else { return }
        displayText = text
    }

    override var acceptsFirstResponder: Bool { true }
    override func becomeFirstResponder() -> Bool { true }

    override func resignFirstResponder() -> Bool {
        isRecording = false
        return super.resignFirstResponder()
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
        addCursorRect(bounds, cursor: isRecording ? .iBeam : .pointingHand)
    }

    override func mouseEntered(with event: NSEvent) { isHovering = true }
    override func mouseExited(with event: NSEvent) { isHovering = false }

    override func mouseDown(with event: NSEvent) {
        isRecording = true
        window?.makeFirstResponder(self)
    }

    override func keyDown(with event: NSEvent) {
        guard isRecording else {
            super.keyDown(with: event)
            return
        }

        if event.keyCode == 53 { // Esc: dinlemeyi bırak
            isRecording = false
            return
        }

        guard let shortcut = Shortcut.from(event: event) else {
            NSSound.beep()
            return
        }

        isRecording = false
        window?.makeFirstResponder(nil)
        onCapture?(shortcut)
    }

    override func draw(_ dirtyRect: NSRect) {
        let rect = bounds.insetBy(dx: 0.5, dy: 0.5)
        let path = NSBezierPath(roundedRect: rect, xRadius: 6, yRadius: 6)

        if isRecording {
            Brand.tint.withAlphaComponent(0.12).setFill()
        } else if isHovering {
            NSColor.labelColor.withAlphaComponent(0.09).setFill()
        } else {
            NSColor.labelColor.withAlphaComponent(0.05).setFill()
        }
        path.fill()

        let strokeColor = isRecording
            ? Brand.tint.withAlphaComponent(0.8)
            : NSColor.separatorColor
        strokeColor.setStroke()
        path.lineWidth = 1
        path.stroke()

        let text = isRecording
            ? L("shortcut.recording", "Press keys…")
            : displayText
        let attributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.monospacedSystemFont(ofSize: 12, weight: .medium),
            .foregroundColor: isRecording ? Brand.tint : NSColor.labelColor
        ]
        let size = (text as NSString).size(withAttributes: attributes)
        (text as NSString).draw(
            at: NSPoint(x: rect.midX - size.width / 2, y: rect.midY - size.height / 2),
            withAttributes: attributes
        )
    }
}
