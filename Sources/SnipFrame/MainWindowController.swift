import AppKit

/// Snipping Tool tarzı ana pencere: uygulamaya tıklayınca açılır, düğmelerle yakalama başlatır.
final class MainWindowController: NSWindowController {

    var onRegion: (() -> Void)?
    var onWindowCapture: (() -> Void)?
    var onFullScreen: (() -> Void)?
    var onDelayChange: ((Double) -> Void)?
    var onShortcutChange: ((Shortcut) -> Void)?
    var onShortcutReset: (() -> Void)?
    var onToolChange: ((EditorTool) -> Void)?

    private let delayValues: [Double] = [0, 3, 5]
    private let delayPopup = NSPopUpButton(frame: .zero, pullsDown: false)
    private let shortcutRecorder = ShortcutRecorderButton(frame: .zero)
    private let primary = PrimaryActionButton(frame: .zero)
    private var toolControl: NSSegmentedControl!

    init() {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 486, height: 448),
            styleMask: [.titled, .closable, .miniaturizable, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )
        window.title = "SnipFrame"
        window.titleVisibility = .hidden
        window.titlebarAppearsTransparent = true
        window.isMovableByWindowBackground = true
        window.backgroundColor = .windowBackgroundColor
        window.isReleasedWhenClosed = false
        window.center()
        super.init(window: window)
        buildContent()
    }

    required init?(coder: NSCoder) { nil }

    // MARK: - Arayüz

    private func buildContent() {
        guard let window, let content = window.contentView else { return }

        let glyph = AppGlyphView()
        glyph.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            glyph.widthAnchor.constraint(equalToConstant: 52),
            glyph.heightAnchor.constraint(equalToConstant: 52)
        ])

        let titleLabel = NSTextField(labelWithString: "SnipFrame")
        titleLabel.font = .systemFont(ofSize: 20, weight: .bold)

        let subtitleLabel = NSTextField(labelWithString: L("app.tagline", "Capture, mark up, share."))
        subtitleLabel.font = .systemFont(ofSize: 12)
        subtitleLabel.textColor = .secondaryLabelColor

        let titles = NSStackView(views: [titleLabel, subtitleLabel])
        titles.orientation = .vertical
        titles.alignment = .leading
        titles.spacing = 2

        let header = NSStackView(views: [glyph, titles])
        header.orientation = .horizontal
        header.alignment = .centerY
        header.spacing = 12

        primary.title = L("action.new", "New Snip")
        primary.target = self
        primary.action = #selector(startRegion)
        primary.keyEquivalent = "\r"
        primary.translatesAutoresizingMaskIntoConstraints = false
        primary.heightAnchor.constraint(equalToConstant: 48).isActive = true

        let windowTile = CaptureTile(frame: .zero)
        windowTile.title = L("action.window", "Window")
        windowTile.symbolName = "macwindow"
        windowTile.caption = L("action.window.caption", "Capture a single window")
        windowTile.target = self
        windowTile.action = #selector(startWindow)

        let screenTile = CaptureTile(frame: .zero)
        screenTile.title = L("action.fullScreen", "Full Screen")
        screenTile.symbolName = "display"
        screenTile.caption = L("action.fullScreen.caption", "Capture the whole display")
        screenTile.target = self
        screenTile.action = #selector(startFullScreen)

        windowTile.translatesAutoresizingMaskIntoConstraints = false
        screenTile.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            windowTile.heightAnchor.constraint(equalToConstant: 92),
            screenTile.heightAnchor.constraint(equalToConstant: 92)
        ])

        let tiles = NSStackView(views: [windowTile, screenTile])
        tiles.orientation = .horizontal
        tiles.spacing = 10
        tiles.distribution = .fillEqually
        tiles.heightAnchor.constraint(equalToConstant: 92).isActive = true

        // Ayarlar: gecikme + kısayol
        // Ayarlar sütunu hizası
        let labelWidth: CGFloat = 86

        // Araç seçimi: editörün başlangıç aracı
        let toolImages = EditorTool.allCases.map { tool in
            NSImage(systemSymbolName: tool.symbolName, accessibilityDescription: tool.title)
                ?? NSImage(size: NSSize(width: 16, height: 16))
        }
        toolControl = NSSegmentedControl(
            images: toolImages,
            trackingMode: .selectOne,
            target: self,
            action: #selector(toolChanged)
        )
        toolControl.segmentStyle = .texturedRounded
        toolControl.selectedSegment = 0
        for (index, tool) in EditorTool.allCases.enumerated() {
            toolControl.setToolTip(tool.title, forSegment: index)
        }

        let toolLabel = makeLabel(L("label.tool", "Tool"))
        toolLabel.translatesAutoresizingMaskIntoConstraints = false
        toolLabel.widthAnchor.constraint(equalToConstant: labelWidth).isActive = true

        let toolSpacer = NSView()
        toolSpacer.setContentHuggingPriority(.defaultLow, for: .horizontal)
        toolSpacer.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)

        let toolRow = NSStackView(views: [toolLabel, toolControl, toolSpacer])
        toolRow.orientation = .horizontal
        toolRow.alignment = .centerY
        toolRow.spacing = 8

        let delayLabel = makeLabel(L("label.delay", "Delay"))
        delayLabel.translatesAutoresizingMaskIntoConstraints = false
        delayLabel.widthAnchor.constraint(equalToConstant: labelWidth).isActive = true

        delayPopup.addItems(withTitles: [
            L("delay.none", "None"),
            L("delay.3", "3 seconds"),
            L("delay.5", "5 seconds")
        ])
        delayPopup.target = self
        delayPopup.action = #selector(delayChanged)
        delayPopup.controlSize = .small
        delayPopup.font = .systemFont(ofSize: 12)

        let delaySpacer = NSView()
        delaySpacer.setContentHuggingPriority(.defaultLow, for: .horizontal)
        delaySpacer.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)

        let delayRow = NSStackView(views: [delayLabel, delayPopup, delaySpacer])
        delayRow.orientation = .horizontal
        delayRow.alignment = .centerY
        delayRow.spacing = 8

        let shortcutLabel = makeLabel(L("label.shortcut", "Shortcut"))
        shortcutLabel.translatesAutoresizingMaskIntoConstraints = false
        shortcutLabel.widthAnchor.constraint(equalToConstant: labelWidth).isActive = true

        shortcutRecorder.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            shortcutRecorder.widthAnchor.constraint(equalToConstant: 132),
            shortcutRecorder.heightAnchor.constraint(equalToConstant: 24)
        ])
        shortcutRecorder.onCapture = { [weak self] shortcut in
            self?.onShortcutChange?(shortcut)
        }

        let resetButton = NSButton(title: L("shortcut.reset", "Reset"), target: self, action: #selector(resetShortcut))
        resetButton.isBordered = false
        resetButton.attributedTitle = NSAttributedString(
            string: L("shortcut.reset", "Reset"),
            attributes: [
                .font: NSFont.systemFont(ofSize: 11, weight: .medium),
                .foregroundColor: Brand.tint.withAlphaComponent(0.85)
            ]
        )

        let shortcutSpacer = NSView()
        shortcutSpacer.setContentHuggingPriority(.defaultLow, for: .horizontal)
        shortcutSpacer.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)

        let shortcutRow = NSStackView(views: [shortcutLabel, shortcutRecorder, resetButton, shortcutSpacer])
        shortcutRow.orientation = .horizontal
        shortcutRow.alignment = .centerY
        shortcutRow.spacing = 8

        // Arşiv: tarihli olarak kaydedilen yakalamaların klasörü.
        let archiveButton = NSButton(
            title: L("action.archive", "Archive"),
            target: self,
            action: #selector(openArchive)
        )
        archiveButton.bezelStyle = .rounded
        archiveButton.controlSize = .small
        archiveButton.font = .systemFont(ofSize: 11)
        archiveButton.image = NSImage(systemSymbolName: "folder", accessibilityDescription: nil)
        archiveButton.imagePosition = .imageLeading
        archiveButton.toolTip = CaptureArchive.folderPath
        archiveButton.translatesAutoresizingMaskIntoConstraints = false

        let archiveSpacer = NSView()
        archiveSpacer.setContentHuggingPriority(.defaultLow, for: .horizontal)
        archiveSpacer.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)

        let archiveRow = NSStackView(views: [archiveButton, archiveSpacer])
        archiveRow.orientation = .horizontal
        archiveRow.alignment = .centerY
        archiveRow.spacing = 8

        let footer = NSStackView(views: [delayRow, shortcutRow, archiveRow])
        footer.orientation = .vertical
        footer.alignment = .leading
        footer.spacing = 8

        let separator = NSBox()
        separator.boxType = .separator
        separator.translatesAutoresizingMaskIntoConstraints = false
        separator.heightAnchor.constraint(equalToConstant: 1).isActive = true

        let stack = NSStackView(views: [header, primary, tiles, toolRow, separator, footer])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 16
        stack.setCustomSpacing(20, after: header)
        stack.setCustomSpacing(14, after: tiles)
        stack.setCustomSpacing(18, after: toolRow)
        stack.setCustomSpacing(12, after: separator)
        stack.translatesAutoresizingMaskIntoConstraints = false
        content.addSubview(stack)

        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 22),
            stack.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -22),
            stack.topAnchor.constraint(equalTo: content.topAnchor, constant: 38),
            primary.widthAnchor.constraint(equalTo: stack.widthAnchor),
            tiles.widthAnchor.constraint(equalTo: stack.widthAnchor),
            separator.widthAnchor.constraint(equalTo: stack.widthAnchor),
            footer.widthAnchor.constraint(equalTo: stack.widthAnchor),
            toolRow.widthAnchor.constraint(equalTo: stack.widthAnchor),
            delayRow.widthAnchor.constraint(equalTo: footer.widthAnchor),
            shortcutRow.widthAnchor.constraint(equalTo: footer.widthAnchor),
            archiveRow.widthAnchor.constraint(equalTo: footer.widthAnchor)
        ])
    }

    private func makeLabel(_ text: String) -> NSTextField {
        let label = NSTextField(labelWithString: text)
        label.font = .systemFont(ofSize: 12)
        label.textColor = .secondaryLabelColor
        return label
    }

    // MARK: - Eylemler

    @objc private func startRegion() { onRegion?() }
    @objc private func startWindow() { onWindowCapture?() }
    @objc private func startFullScreen() { onFullScreen?() }
    @objc private func openArchive() { CaptureArchive.openFolder() }
    @objc private func resetShortcut() {
        shortcutRecorder.setShortcutText(Shortcut.default.displayString)
        onShortcutReset?()
    }

    @objc private func toolChanged() {
        let tool = EditorTool(rawValue: toolControl.selectedSegment) ?? .pen
        onToolChange?(tool)
    }

    @objc private func delayChanged() {
        let index = delayPopup.indexOfSelectedItem
        guard delayValues.indices.contains(index) else { return }
        onDelayChange?(delayValues[index])
    }

    func setDelay(_ seconds: Double) {
        if let index = delayValues.firstIndex(of: seconds) {
            delayPopup.selectItem(at: index)
        }
    }

    func setTool(_ tool: EditorTool) {
        toolControl.selectedSegment = tool.rawValue
    }

    func setShortcutText(_ text: String) {
        shortcutRecorder.setShortcutText(text)
        primary.hintText = text
        primary.needsDisplay = true
    }
}
