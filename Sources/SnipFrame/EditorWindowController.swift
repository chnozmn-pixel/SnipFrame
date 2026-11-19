import AppKit
import UniformTypeIdentifiers

final class EditorWindowController: NSWindowController, NSWindowDelegate, EditorCanvasDelegate {

    private let canvas: EditorCanvasView
    private var toolControl: NSSegmentedControl!
    private var colorWell: NSColorWell!
    private var widthSlider: NSSlider!
    private var statusLabel: NSTextField!

    var onClose: (() -> Void)?

    /// Logoya uygun varsayılan çizim rengi (sabit: dışa aktarımda da aynı kalsın).
    static let defaultStrokeColor = NSColor(srgbRed: 0.11, green: 0.11, blue: 0.13, alpha: 1)

    private let initialTool: EditorTool
    private let initialColor: NSColor

    init?(image: NSImage,
          tool: EditorTool = .pen,
          strokeColor: NSColor = EditorWindowController.defaultStrokeColor) {
        guard let cgImage = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return nil }

        initialTool = tool
        initialColor = strokeColor
        canvas = EditorCanvasView(baseImage: image, baseCGImage: cgImage)

        let visible = NSScreen.main?.visibleFrame.size ?? NSSize(width: 1200, height: 800)
        let toolbarHeight: CGFloat = 46
        let width = min(max(image.size.width, 480), visible.width * 0.9)
        let height = min(max(image.size.height, 240), visible.height * 0.9 - toolbarHeight) + toolbarHeight

        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: width, height: height),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = L("editor.title", "SnipFrame — Edit")
        window.isReleasedWhenClosed = false
        window.center()

        super.init(window: window)

        window.delegate = self
        canvas.delegate = self
        canvas.tool = tool
        canvas.strokeColor = strokeColor
        buildContent()
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    // MARK: - UI

    private func buildContent() {
        guard let window, let content = window.contentView else { return }

        let toolbar = NSStackView()
        toolbar.orientation = .horizontal
        toolbar.alignment = .centerY
        toolbar.spacing = 10
        toolbar.distribution = .fill
        toolbar.translatesAutoresizingMaskIntoConstraints = false

        let images = EditorTool.allCases.map { tool in
            NSImage(systemSymbolName: tool.symbolName, accessibilityDescription: tool.title)
                ?? NSImage(size: NSSize(width: 16, height: 16))
        }
        toolControl = NSSegmentedControl(images: images, trackingMode: .selectOne, target: self, action: #selector(toolChanged(_:)))
        toolControl.selectedSegment = initialTool.rawValue
        toolControl.segmentStyle = .texturedRounded
        for (index, tool) in EditorTool.allCases.enumerated() {
            toolControl.setToolTip(tool.title, forSegment: index)
        }

        colorWell = NSColorWell()
        colorWell.color = initialColor
        colorWell.target = self
        colorWell.action = #selector(colorChanged(_:))
        colorWell.translatesAutoresizingMaskIntoConstraints = false
        colorWell.widthAnchor.constraint(equalToConstant: 36).isActive = true
        colorWell.heightAnchor.constraint(equalToConstant: 24).isActive = true

        widthSlider = NSSlider(value: 3, minValue: 1, maxValue: 12, target: self, action: #selector(widthChanged(_:)))
        widthSlider.translatesAutoresizingMaskIntoConstraints = false
        widthSlider.widthAnchor.constraint(equalToConstant: 90).isActive = true
        widthSlider.toolTip = L("editor.lineWidth", "Line width")

        let spacer = NSView()
        spacer.setContentHuggingPriority(.defaultLow, for: .horizontal)
        spacer.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)

        statusLabel = NSTextField(labelWithString: "")
        statusLabel.font = .systemFont(ofSize: 11)
        statusLabel.textColor = .secondaryLabelColor

        let copyButton = NSButton(title: L("editor.copy", "Copy"), target: self, action: #selector(copyImage))
        copyButton.bezelStyle = .rounded
        copyButton.toolTip = L("editor.copy.tooltip", "Copy image to clipboard (⌘C)")

        let saveButton = NSButton(title: L("editor.save", "Save…"), target: self, action: #selector(saveImage))
        saveButton.bezelStyle = .rounded
        saveButton.toolTip = L("editor.save.tooltip", "Save as PNG (⌘S)")

        [toolControl, colorWell, widthSlider, spacer, statusLabel, copyButton, saveButton].forEach {
            toolbar.addArrangedSubview($0)
        }

        let scroll = NSScrollView()
        scroll.translatesAutoresizingMaskIntoConstraints = false
        scroll.hasHorizontalScroller = true
        scroll.hasVerticalScroller = true
        scroll.allowsMagnification = true
        scroll.minMagnification = 0.2
        scroll.borderType = .noBorder
        scroll.drawsBackground = true
        scroll.backgroundColor = .underPageBackgroundColor

        // A flipped container keeps the scroll view anchored at the top-left of the image.
        let container = CanvasContainerView(frame: NSRect(origin: .zero, size: canvas.frame.size))
        canvas.frame = NSRect(origin: .zero, size: canvas.frame.size)
        container.addSubview(canvas)
        scroll.documentView = container

        content.addSubview(toolbar)
        content.addSubview(scroll)

        NSLayoutConstraint.activate([
            toolbar.topAnchor.constraint(equalTo: content.topAnchor),
            toolbar.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 10),
            toolbar.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -10),
            toolbar.heightAnchor.constraint(equalToConstant: 46),
            scroll.topAnchor.constraint(equalTo: toolbar.bottomAnchor),
            scroll.leadingAnchor.constraint(equalTo: content.leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: content.trailingAnchor),
            scroll.bottomAnchor.constraint(equalTo: content.bottomAnchor)
        ])
    }

    // MARK: - Actions

    @objc private func toolChanged(_ sender: NSSegmentedControl) {
        canvas.tool = EditorTool(rawValue: sender.selectedSegment) ?? .pen
    }

    @objc private func colorChanged(_ sender: NSColorWell) {
        canvas.strokeColor = sender.color
    }

    @objc private func widthChanged(_ sender: NSSlider) {
        canvas.lineWidth = CGFloat(sender.doubleValue)
    }

    @objc func copyImage() {
        guard let rep = canvas.renderBitmap() else { return }
        let image = NSImage(size: rep.size)
        image.addRepresentation(rep)

        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        pasteboard.writeObjects([image])
        flash(L("editor.copied", "Copied to clipboard ✓"))
    }

    @objc func saveImage() {
        guard let rep = canvas.renderBitmap() else { return }

        let panel = NSSavePanel()
        panel.allowedContentTypes = [.png]
        panel.canCreateDirectories = true
        panel.nameFieldStringValue = Self.defaultFileName()
        panel.directoryURL = FileManager.default.urls(for: .desktopDirectory, in: .userDomainMask).first

        guard let window else { return }
        panel.beginSheetModal(for: window) { response in
            guard response == .OK, let url = panel.url,
                  let data = rep.representation(using: .png, properties: [:]) else { return }
            do {
                try data.write(to: url)
            } catch {
                let alert = NSAlert()
                alert.messageText = L("editor.saveFailed", "Could not save")
                alert.informativeText = error.localizedDescription
                alert.runModal()
            }
        }
    }

    private func flash(_ text: String) {
        statusLabel.stringValue = text
        NSObject.cancelPreviousPerformRequests(withTarget: self, selector: #selector(clearStatus), object: nil)
        perform(#selector(clearStatus), with: nil, afterDelay: 2.0)
    }

    @objc private func clearStatus() {
        statusLabel.stringValue = ""
    }

    private static func defaultFileName() -> String {
        let formatter = DateFormatter()
        formatter.dateFormat = L("date.format", "yyyy-MM-dd 'at' HH-mm-ss")
        formatter.locale = Locale.current
        return String(format: L("save.fileName", "Screenshot %@.png"), formatter.string(from: Date()))
    }

    // MARK: - EditorCanvasDelegate

    func canvasDidEdit(_ canvas: EditorCanvasView) {
        // Hook for future toolbar state (undo/redo buttons).
    }

    func canvasDidRequestCopy(_ canvas: EditorCanvasView) {
        copyImage()
    }

    func canvasDidRequestSave(_ canvas: EditorCanvasView) {
        saveImage()
    }

    // MARK: - NSWindowDelegate

    func windowWillClose(_ notification: Notification) {
        onClose?()
    }
}
