import AppKit

protocol EditorCanvasDelegate: AnyObject {
    func canvasDidEdit(_ canvas: EditorCanvasView)
    func canvasDidRequestCopy(_ canvas: EditorCanvasView)
    func canvasDidRequestSave(_ canvas: EditorCanvasView)
}

/// The annotation canvas. Coordinates are in points; export renders at native pixels.
final class EditorCanvasView: NSView, NSTextFieldDelegate {

    let baseImage: NSImage
    let baseCGImage: CGImage
    weak var delegate: EditorCanvasDelegate?

    var tool: EditorTool = .pen {
        didSet {
            if tool != .text { finishTextEditing() }
        }
    }
    var strokeColor: NSColor = .systemRed
    var lineWidth: CGFloat = 3

    private(set) var annotations: [Annotation] = []
    private var redoStack: [Annotation] = []
    private var current: Annotation?
    private var dragStart: CGPoint = .zero
    private var activeTextField: NSTextField?

    init(baseImage: NSImage, baseCGImage: CGImage) {
        self.baseImage = baseImage
        self.baseCGImage = baseCGImage
        super.init(frame: NSRect(origin: .zero, size: baseImage.size))
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override var acceptsFirstResponder: Bool { true }
    override var isFlipped: Bool { false }

    var renderer: CanvasRenderer {
        CanvasRenderer(
            baseImage: baseImage,
            baseCGImage: baseCGImage,
            pointSize: baseImage.size,
            scale: CGFloat(baseCGImage.width) / max(1, baseImage.size.width)
        )
    }

    // MARK: - Drawing

    override func draw(_ dirtyRect: NSRect) {
        let items = annotations + (current.map { [$0] } ?? [])
        renderer.draw(in: bounds, annotations: items)
    }

    // MARK: - Mouse

    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)
        let point = convert(event.locationInWindow, from: nil)

        if tool == .text {
            beginTextEditing(at: point)
            return
        }

        dragStart = point
        current = Annotation(tool: tool, points: [point, point], color: strokeColor, lineWidth: lineWidth)
        needsDisplay = true
    }

    override func mouseDragged(with event: NSEvent) {
        guard var annotation = current else { return }
        let point = convert(event.locationInWindow, from: nil)

        if annotation.tool == .pen || annotation.tool == .highlighter {
            annotation.points.append(point)
        } else {
            annotation.points = [dragStart, point]
        }
        current = annotation
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        guard let annotation = current else { return }
        current = nil

        if annotation.tool == .pen || annotation.tool == .highlighter {
            if annotation.points.count > 1 { commit(annotation) }
        } else if let a = annotation.points.first, let b = annotation.points.last,
                  abs(a.x - b.x) > 2 || abs(a.y - b.y) > 2 {
            commit(annotation)
        }
        needsDisplay = true
    }

    private func commit(_ annotation: Annotation) {
        annotations.append(annotation)
        redoStack.removeAll()
        delegate?.canvasDidEdit(self)
        needsDisplay = true
    }

    @objc func undo() {
        guard let last = annotations.popLast() else { return }
        redoStack.append(last)
        delegate?.canvasDidEdit(self)
        needsDisplay = true
    }

    @objc func redo() {
        guard let annotation = redoStack.popLast() else { return }
        annotations.append(annotation)
        delegate?.canvasDidEdit(self)
        needsDisplay = true
    }

    // MARK: - Text tool

    private func beginTextEditing(at point: CGPoint) {
        finishTextEditing()

        let fontSize = max(14, lineWidth * 5)
        let field = NSTextField(frame: NSRect(x: point.x, y: point.y - 14, width: 240, height: fontSize + 12))
        field.isBordered = false
        field.drawsBackground = false
        field.focusRingType = .none
        field.font = .systemFont(ofSize: fontSize, weight: .semibold)
        field.textColor = strokeColor
        field.placeholderString = L("editor.textPlaceholder", "Type and press Enter")
        field.delegate = self
        field.target = self
        field.action = #selector(textFieldAction(_:))
        addSubview(field)
        activeTextField = field
        window?.makeFirstResponder(field)
    }

    @objc private func textFieldAction(_ sender: NSTextField) {
        finishTextEditing()
    }

    func controlTextDidEndEditing(_ obj: Notification) {
        finishTextEditing()
    }

    private func finishTextEditing() {
        guard let field = activeTextField else { return }
        activeTextField = nil

        let text = field.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        let origin = CGPoint(x: field.frame.minX, y: field.frame.minY + 4)
        let color = field.textColor ?? strokeColor
        let fontSize = field.font?.pointSize ?? 18
        field.removeFromSuperview()

        guard !text.isEmpty else {
            needsDisplay = true
            return
        }
        commit(Annotation(tool: .text, points: [origin], color: color, lineWidth: fontSize, text: text))
    }

    // MARK: - Keyboard shortcuts

    override func keyDown(with event: NSEvent) {
        if event.modifierFlags.contains(.command) {
            switch event.charactersIgnoringModifiers?.lowercased() {
            case "c":
                delegate?.canvasDidRequestCopy(self)
                return
            case "s":
                delegate?.canvasDidRequestSave(self)
                return
            case "z":
                if event.modifierFlags.contains(.shift) { redo() } else { undo() }
                return
            default:
                break
            }
        }

        switch event.keyCode {
        case 53: // Escape
            window?.performClose(nil)
        case 36, 76: // Return / Enter
            delegate?.canvasDidRequestCopy(self)
        default:
            super.keyDown(with: event)
        }
    }

    // MARK: - Export

    func renderBitmap() -> NSBitmapImageRep? {
        let scale = renderer.scale
        let width = Int((baseImage.size.width * scale).rounded())
        let height = Int((baseImage.size.height * scale).rounded())
        guard width > 0, height > 0,
              let rep = NSBitmapImageRep(
                bitmapDataPlanes: nil,
                pixelsWide: width,
                pixelsHigh: height,
                bitsPerSample: 8,
                samplesPerPixel: 4,
                hasAlpha: true,
                isPlanar: false,
                colorSpaceName: .deviceRGB,
                bytesPerRow: 0,
                bitsPerPixel: 0
              ),
              let context = NSGraphicsContext(bitmapImageRep: rep) else { return nil }

        rep.size = baseImage.size
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = context
        context.cgContext.scaleBy(x: scale, y: scale)
        renderer.draw(in: NSRect(origin: .zero, size: baseImage.size), annotations: annotations)
        NSGraphicsContext.restoreGraphicsState()
        return rep
    }

    func flattenedImage() -> NSImage {
        let image = NSImage(size: baseImage.size)
        if let rep = renderBitmap() {
            image.addRepresentation(rep)
        } else {
            baseImage.draw(in: NSRect(origin: .zero, size: baseImage.size))
        }
        return image
    }
}
