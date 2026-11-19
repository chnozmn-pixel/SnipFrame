import AppKit

protocol EditorCanvasDelegate: AnyObject {
    func canvasDidEdit(_ canvas: EditorCanvasView)
    func canvasDidRequestCopy(_ canvas: EditorCanvasView)
    func canvasDidRequestSave(_ canvas: EditorCanvasView)
}

/// The annotation canvas. Coordinates are in points; export renders at native pixels.
final class EditorCanvasView: NSView, NSTextFieldDelegate, NSUserInterfaceValidations {

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
    private var undoStack: [[Annotation]] = []
    private let historyLimit = 100
    private var redoStack: [[Annotation]] = []
    private var erasing = false
    private var eraseSnapshot: [Annotation] = []
    private var didErase = false
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

        if tool == .eraser {
            erasing = true
            didErase = false
            eraseSnapshot = annotations
            erase(at: point)
            needsDisplay = true
            return
        }

        dragStart = point
        current = Annotation(tool: tool, points: [point, point], color: strokeColor, lineWidth: lineWidth)
        needsDisplay = true
    }

    override func mouseDragged(with event: NSEvent) {
        if erasing {
            erase(at: convert(event.locationInWindow, from: nil))
            needsDisplay = true
            return
        }

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
        if erasing {
            erasing = false
            if didErase {
                pushHistory(eraseSnapshot)
                redoStack.removeAll()
                delegate?.canvasDidEdit(self)
            }
            eraseSnapshot = []
            didErase = false
            needsDisplay = true
            return
        }

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

    var canUndo: Bool { !undoStack.isEmpty }
    var canRedo: Bool { !redoStack.isEmpty }

    private func pushHistory(_ snapshot: [Annotation]) {
        undoStack.append(snapshot)
        if undoStack.count > historyLimit {
            undoStack.removeFirst(undoStack.count - historyLimit)
        }
    }

    private func commit(_ annotation: Annotation) {
        pushHistory(annotations)
        annotations.append(annotation)
        redoStack.removeAll()
        delegate?.canvasDidEdit(self)
        needsDisplay = true
    }

    /// Menü ve araç çubuğu için: metin düzenlenirken alan editörü önceliklidir.
    @objc func undo(_ sender: Any?) { undo() }

    @objc func redo(_ sender: Any?) { redo() }

    func validateUserInterfaceItem(_ item: NSValidatedUserInterfaceItem) -> Bool {
        if item.action == #selector(undo(_:)) { return canUndo }
        if item.action == #selector(redo(_:)) { return canRedo }
        return true
    }

    @objc func undo() {
        guard let previous = undoStack.popLast() else { return }
        redoStack.append(annotations)
        annotations = previous
        delegate?.canvasDidEdit(self)
        needsDisplay = true
    }

    @objc func redo() {
        guard let next = redoStack.popLast() else { return }
        undoStack.append(annotations)
        annotations = next
        delegate?.canvasDidEdit(self)
        needsDisplay = true
    }

    // MARK: - Eraser

    /// Silgi: çizgileri temas noktasından böler, kapalı şekilleri/metni tümüyle kaldırır.
    private func erase(at point: CGPoint) {
        var result: [Annotation] = []
        var changed = false

        for annotation in annotations {
            let isHighlighter = annotation.tool == .highlighter
            let radius = max(7, annotation.lineWidth * (isHighlighter ? 4.5 : 1.8))

            switch annotation.tool {
            case .pen, .highlighter:
                let runs = remainingRuns(in: annotation, around: point, radius: radius)
                if runs.count == 1, runs[0].count == annotation.points.count {
                    result.append(annotation)
                } else {
                    changed = true
                    for run in runs {
                        var piece = annotation
                        piece.points = run
                        result.append(piece)
                    }
                }
            case .eraser:
                result.append(annotation)
            default:
                if hit(annotation, at: point, radius: radius) {
                    changed = true
                } else {
                    result.append(annotation)
                }
            }
        }

        if changed {
            annotations = result
            didErase = true
        }
    }

    private func remainingRuns(in annotation: Annotation, around center: CGPoint, radius: CGFloat) -> [[CGPoint]] {
        var runs: [[CGPoint]] = []
        var run: [CGPoint] = []
        for point in annotation.points {
            if hypot(point.x - center.x, point.y - center.y) <= radius {
                if run.count > 1 { runs.append(run) }
                run = []
            } else {
                run.append(point)
            }
        }
        if run.count > 1 { runs.append(run) }
        return runs
    }

    private func hit(_ annotation: Annotation, at point: CGPoint, radius: CGFloat) -> Bool {
        let pad = radius + annotation.lineWidth
        switch annotation.tool {
        case .arrow:
            guard annotation.points.count > 1 else { return false }
            return distance(from: point, toSegment: annotation.points[0], annotation.points[1]) <= pad
        case .rectangle:
            guard annotation.points.count > 1 else { return false }
            let rect = rect(from: annotation.points[0], to: annotation.points[1])
            let corners = [
                CGPoint(x: rect.minX, y: rect.minY), CGPoint(x: rect.maxX, y: rect.minY),
                CGPoint(x: rect.maxX, y: rect.maxY), CGPoint(x: rect.minX, y: rect.maxY)
            ]
            for index in 0..<4 {
                if distance(from: point, toSegment: corners[index], corners[(index + 1) % 4]) <= pad { return true }
            }
            return false
        case .ellipse:
            guard annotation.points.count > 1 else { return false }
            let rect = rect(from: annotation.points[0], to: annotation.points[1])
            let center = CGPoint(x: rect.midX, y: rect.midY)
            let radiusX = rect.width / 2, radiusY = rect.height / 2
            guard radiusX > 0.5, radiusY > 0.5 else { return false }
            for step in 0..<96 {
                let angle = CGFloat(step) / 96 * 2 * .pi
                let sample = CGPoint(x: center.x + radiusX * cos(angle), y: center.y + radiusY * sin(angle))
                if hypot(sample.x - point.x, sample.y - point.y) <= pad { return true }
            }
            return false
        case .pixelate:
            guard annotation.points.count > 1 else { return false }
            return rect(from: annotation.points[0], to: annotation.points[1]).insetBy(dx: -2, dy: -2).contains(point)
        case .text:
            guard let text = annotation.text, !text.isEmpty else { return false }
            let font = NSFont.systemFont(ofSize: max(10, annotation.lineWidth), weight: .semibold)
            let size = (text as NSString).size(withAttributes: [.font: font])
            let origin = annotation.points[0]
            return NSRect(x: origin.x, y: origin.y, width: size.width, height: size.height)
                .insetBy(dx: -4, dy: -4)
                .contains(point)
        case .pen, .highlighter, .eraser:
            return false
        }
    }

    private func rect(from a: CGPoint, to b: CGPoint) -> NSRect {
        NSRect(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(a.x - b.x), height: abs(a.y - b.y))
    }

    private func distance(from point: CGPoint, toSegment a: CGPoint, _ b: CGPoint) -> CGFloat {
        let dx = b.x - a.x, dy = b.y - a.y
        let lengthSquared = dx * dx + dy * dy
        guard lengthSquared > 0 else { return hypot(point.x - a.x, point.y - a.y) }
        let t = max(0, min(1, ((point.x - a.x) * dx + (point.y - a.y) * dy) / lengthSquared))
        return hypot(point.x - (a.x + t * dx), point.y - (a.y + t * dy))
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
