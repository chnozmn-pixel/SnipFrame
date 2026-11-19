import AppKit

enum EditorTool: Int, CaseIterable {
    case pen, highlighter, arrow, rectangle, ellipse, pixelate, text

    var title: String {
        switch self {
        case .pen: return L("tool.pen", "Pen")
        case .highlighter: return L("tool.highlighter", "Highlighter")
        case .arrow: return L("tool.arrow", "Arrow")
        case .rectangle: return L("tool.rectangle", "Rectangle")
        case .ellipse: return L("tool.ellipse", "Ellipse")
        case .pixelate: return L("tool.pixelate", "Mosaic")
        case .text: return L("tool.text", "Text")
        }
    }

    var symbolName: String {
        switch self {
        case .pen: return "pencil.tip"
        case .highlighter: return "highlighter"
        case .arrow: return "arrow.up.right"
        case .rectangle: return "rectangle"
        case .ellipse: return "oval"
        case .pixelate: return "squareshape.split.3x3"
        case .text: return "textformat"
        }
    }
}

/// One committed (or in-progress) drawing operation, stored in view points.
struct Annotation {
    var tool: EditorTool
    var points: [CGPoint]
    var color: NSColor
    var lineWidth: CGFloat
    var text: String?

    var bounds: NSRect {
        guard let first = points.first else { return .zero }
        var rect = NSRect(origin: first, size: .zero)
        for point in points.dropFirst() {
            rect = rect.union(NSRect(origin: point, size: .zero))
        }
        return rect
    }
}

/// Renders the base screenshot plus annotations into any graphics context.
struct CanvasRenderer {
    let baseImage: NSImage
    let baseCGImage: CGImage
    let pointSize: CGSize
    let scale: CGFloat

    func draw(in rect: NSRect, annotations: [Annotation]) {
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current?.imageInterpolation = .high
        baseImage.draw(in: rect, from: .zero, operation: .sourceOver, fraction: 1)
        NSGraphicsContext.restoreGraphicsState()

        for annotation in annotations {
            draw(annotation)
        }
    }

    private func draw(_ annotation: Annotation) {
        guard let first = annotation.points.first else { return }

        switch annotation.tool {
        case .pen, .highlighter:
            guard annotation.points.count > 1 else { return }
            let path = NSBezierPath()
            path.move(to: first)
            for point in annotation.points.dropFirst() { path.line(to: point) }
            let isHighlighter = annotation.tool == .highlighter
            (isHighlighter ? annotation.color.withAlphaComponent(0.35) : annotation.color).setStroke()
            path.lineWidth = isHighlighter ? annotation.lineWidth * 5 : annotation.lineWidth
            path.lineCapStyle = isHighlighter ? .butt : .round
            path.lineJoinStyle = .round
            path.stroke()

        case .arrow:
            guard annotation.points.count > 1 else { return }
            let start = annotation.points[0]
            let end = annotation.points[1]
            guard hypot(end.x - start.x, end.y - start.y) > 3 else { return }

            let shaft = NSBezierPath()
            shaft.move(to: start)
            shaft.line(to: end)
            annotation.color.setStroke()
            shaft.lineWidth = annotation.lineWidth
            shaft.lineCapStyle = .round
            shaft.stroke()

            let angle = atan2(end.y - start.y, end.x - start.x)
            let headLength = max(10, annotation.lineWidth * 4)
            let spread = CGFloat.pi / 7
            let left = CGPoint(x: end.x - headLength * cos(angle - spread),
                               y: end.y - headLength * sin(angle - spread))
            let right = CGPoint(x: end.x - headLength * cos(angle + spread),
                                y: end.y - headLength * sin(angle + spread))
            let head = NSBezierPath()
            head.move(to: end)
            head.line(to: left)
            head.move(to: end)
            head.line(to: right)
            head.lineWidth = annotation.lineWidth
            head.lineCapStyle = .round
            head.stroke()

        case .rectangle, .ellipse, .pixelate:
            guard annotation.points.count > 1 else { return }
            let rect = Self.rect(from: annotation.points[0], to: annotation.points[1])
            guard rect.width > 1, rect.height > 1 else { return }
            if annotation.tool == .pixelate {
                drawMosaic(in: rect)
            } else {
                let path = annotation.tool == .rectangle ? NSBezierPath(rect: rect) : NSBezierPath(ovalIn: rect)
                annotation.color.setStroke()
                path.lineWidth = annotation.lineWidth
                path.stroke()
            }

        case .text:
            guard let text = annotation.text, !text.isEmpty else { return }
            let font = NSFont.systemFont(ofSize: max(10, annotation.lineWidth), weight: .semibold)
            let attributes: [NSAttributedString.Key: Any] = [
                .font: font,
                .foregroundColor: annotation.color
            ]
            (text as NSString).draw(at: first, withAttributes: attributes)
        }
    }

    private func drawMosaic(in rect: NSRect) {
        let blockSize: CGFloat = 12
        let pixelRect = CGRect(
            x: rect.minX * scale,
            y: (pointSize.height - rect.maxY) * scale,
            width: rect.width * scale,
            height: rect.height * scale
        ).integral
        guard pixelRect.width > 2, pixelRect.height > 2,
              let cropped = baseCGImage.cropping(to: pixelRect) else { return }

        let smallWidth = max(1, Int(pixelRect.width / blockSize))
        let smallHeight = max(1, Int(pixelRect.height / blockSize))
        guard let context = CGContext(
            data: nil,
            width: smallWidth,
            height: smallHeight,
            bitsPerComponent: 8,
            bytesPerRow: 0,
            space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        ) else { return }

        context.interpolationQuality = .medium
        context.draw(cropped, in: CGRect(x: 0, y: 0, width: smallWidth, height: smallHeight))
        guard let smallImage = context.makeImage() else { return }

        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current?.imageInterpolation = .none
        NSImage(cgImage: smallImage, size: rect.size).draw(in: rect)
        NSGraphicsContext.restoreGraphicsState()
    }

    static func rect(from a: CGPoint, to b: CGPoint) -> NSRect {
        NSRect(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(a.x - b.x), height: abs(a.y - b.y))
    }
}
