import AppKit
import Carbon.HIToolbox

final class AppDelegate: NSObject, NSApplicationDelegate {

    private var statusItem: NSStatusItem!
    private var mainWindow: MainWindowController?
    private var overlay: CaptureOverlayController?
    private var editor: EditorWindowController?
    private var delayMenuItems: [NSMenuItem] = []
    private var regionMenuItem: NSMenuItem?
    private var delay: TimeInterval = UserDefaults.standard.double(forKey: "captureDelay")
    private var shortcut = ShortcutStore.current
    private var defaultTool = EditorTool(rawValue: UserDefaults.standard.integer(forKey: "editorTool")) ?? .pen

    // MARK: - Lifecycle

    func applicationDidFinishLaunching(_ notification: Notification) {
        setupStatusItem()
        setupMainMenu()
        setupMainWindow()
        showMainWindow(nil)
        registerHotkey()

    }

    func applicationWillTerminate(_ notification: Notification) {
        HotkeyManager.shared.unregister()
    }

    /// Dock simgesine tıklanınca ana pencereyi geri getirir.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showMainWindow(nil)
        return true
    }

    // MARK: - Ana pencere

    private func setupMainWindow() {
        let controller = MainWindowController()
        controller.onRegion = { [weak self] in self?.snipRegion() }
        controller.onWindowCapture = { [weak self] in self?.snipWindow() }
        controller.onFullScreen = { [weak self] in self?.captureFullScreen() }
        controller.onDelayChange = { [weak self] seconds in self?.applyDelay(seconds) }
        controller.onShortcutChange = { [weak self] shortcut in self?.applyShortcut(shortcut) }
        controller.onShortcutReset = { [weak self] in self?.applyShortcut(.default) }
        controller.onToolChange = { [weak self] tool in self?.applyTool(tool) }
        controller.setDelay(delay)
        controller.setTool(defaultTool)
        controller.setShortcutText(shortcut.displayString)
        mainWindow = controller
    }

    @objc private func showMainWindow(_ sender: Any?) {
        NSApp.activate(ignoringOtherApps: true)
        mainWindow?.showWindow(nil)
        mainWindow?.window?.makeKeyAndOrderFront(nil)
    }

    /// Kendi pencereleri yakalamaya girmeden önce gizler.
    private func hideOwnWindows() {
        mainWindow?.window?.orderOut(nil)
        editor?.window?.orderOut(nil)
    }

    // MARK: - Kısayol

    @discardableResult
    private func registerHotkey() -> Bool {
        let registered = HotkeyManager.shared.register(shortcut) { [weak self] in
            self?.snipRegion()
        }
        updateShortcutUI()
        return registered
    }

    private func updateShortcutUI() {
        let display = shortcut.displayString
        statusItem?.button?.toolTip = String(
            format: L("status.tooltip", "SnipFrame — %@ for a region snip"),
            display
        )
        regionMenuItem?.title = "\(L("menu.region", "Region Snip")) (\(display))"
        mainWindow?.setShortcutText(display)
    }

    private func applyShortcut(_ newShortcut: Shortcut) {
        let previous = shortcut
        shortcut = newShortcut
        ShortcutStore.save(newShortcut)

        if registerHotkey() { return }

        shortcut = previous
        ShortcutStore.save(previous)
        registerHotkey()
        presentShortcutConflict()
    }

    // MARK: - Menü çubuğu

    private func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let button = statusItem.button {
            button.image = StatusIcon.make()
        }

        let menu = NSMenu()

        let openWindow = NSMenuItem(
            title: L("menu.openWindow", "Open Main Window"),
            action: #selector(showMainWindow(_:)),
            keyEquivalent: ""
        )
        openWindow.target = self
        menu.addItem(openWindow)
        menu.addItem(.separator())

        let region = NSMenuItem(
            title: L("menu.region", "Region Snip"),
            action: #selector(snipRegion),
            keyEquivalent: ""
        )
        region.target = self
        menu.addItem(region)
        regionMenuItem = region

        let windowItem = NSMenuItem(
            title: L("menu.window", "Window Snip"),
            action: #selector(snipWindow),
            keyEquivalent: ""
        )
        windowItem.target = self
        menu.addItem(windowItem)

        let fullScreen = NSMenuItem(
            title: L("menu.fullScreen", "Full Screen Snip"),
            action: #selector(captureFullScreen),
            keyEquivalent: ""
        )
        fullScreen.target = self
        menu.addItem(fullScreen)

        menu.addItem(.separator())

        let delayItem = NSMenuItem(title: L("menu.delay", "Delay"), action: nil, keyEquivalent: "")
        let delayMenu = NSMenu()
        let delayOptions: [(String, Double)] = [
            (L("delay.none", "None"), 0),
            (L("delay.3", "3 seconds"), 3),
            (L("delay.5", "5 seconds"), 5)
        ]
        for (title, seconds) in delayOptions {
            let item = NSMenuItem(title: title, action: #selector(setDelay(_:)), keyEquivalent: "")
            item.target = self
            item.tag = Int(seconds)
            item.state = (delay == seconds) ? .on : .off
            delayMenu.addItem(item)
            delayMenuItems.append(item)
        }
        delayItem.submenu = delayMenu
        menu.addItem(delayItem)

        menu.addItem(.separator())

        let about = NSMenuItem(
            title: L("menu.about", "About SnipFrame"),
            action: #selector(showAbout),
            keyEquivalent: ""
        )
        about.target = self
        menu.addItem(about)

        let quit = NSMenuItem(title: L("menu.quit", "Quit"), action: #selector(quit), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)

        statusItem.menu = menu
    }

    /// Editör penceresi içindeki standart kısayollar için ana menü.
    private func setupMainMenu() {
        let mainMenu = NSMenu()

        let appMenuItem = NSMenuItem()
        let appMenu = NSMenu()
        let aboutItem = appMenu.addItem(
            withTitle: L("menu.about", "About SnipFrame"),
            action: #selector(showAbout),
            keyEquivalent: ""
        )
        aboutItem.target = self

        let windowItem = appMenu.addItem(
            withTitle: L("menu.openWindow", "Open Main Window"),
            action: #selector(showMainWindow(_:)),
            keyEquivalent: "0"
        )
        windowItem.target = self

        let newSnipItem = appMenu.addItem(
            withTitle: L("action.new", "New Snip"),
            action: #selector(snipRegion),
            keyEquivalent: "n"
        )
        newSnipItem.target = self
        appMenu.addItem(.separator())

        appMenu.addItem(withTitle: L("menu.hide", "Hide SnipFrame"), action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        let quitItem = appMenu.addItem(withTitle: L("menu.quit", "Quit"), action: #selector(quit), keyEquivalent: "q")
        quitItem.target = self
        appMenuItem.submenu = appMenu
        mainMenu.addItem(appMenuItem)

        let editMenuItem = NSMenuItem()
        let editMenu = NSMenu(title: L("menu.edit", "Edit"))
        editMenu.addItem(withTitle: L("menu.undo", "Undo"), action: #selector(EditorCanvasView.undo(_:)), keyEquivalent: "z")
        let redoItem = editMenu.addItem(withTitle: L("menu.redo", "Redo"), action: #selector(EditorCanvasView.redo(_:)), keyEquivalent: "z")
        redoItem.keyEquivalentModifierMask = [.command, .shift]
        editMenu.addItem(.separator())
        editMenu.addItem(withTitle: L("menu.copy", "Copy"), action: #selector(EditorWindowController.copyImage), keyEquivalent: "c")
        editMenu.addItem(withTitle: L("menu.save", "Save…"), action: #selector(EditorWindowController.saveImage), keyEquivalent: "s")
        editMenuItem.submenu = editMenu
        mainMenu.addItem(editMenuItem)

        let windowMenuItem = NSMenuItem()
        let windowMenu = NSMenu(title: L("menu.windowMenu", "Window"))
        windowMenu.addItem(withTitle: L("menu.minimize", "Minimize"), action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        windowMenu.addItem(withTitle: L("menu.close", "Close"), action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        windowMenuItem.submenu = windowMenu
        mainMenu.addItem(windowMenuItem)

        NSApp.mainMenu = mainMenu
    }

    // MARK: - Yakalama

    @objc private func snipRegion() {
        startCapture(mode: .region)
    }

    @objc private func snipWindow() {
        startCapture(mode: .window)
    }

    private func startCapture(mode: CaptureOverlayController.Mode) {
        guard ensureScreenPermission() else { return }
        hideOwnWindows()

        let work: () -> Void = { [weak self] in
            guard let self else { return }
            let screen = ScreenCaptureService.screenUnderMouse()
            guard let screenshot = ScreenCaptureService.capture(screen) else {
                self.presentPermissionAlert()
                self.showMainWindow(nil)
                return
            }
            let controller = CaptureOverlayController(
                screen: screen,
                screenshot: screenshot,
                mode: mode,
                onFinish: { [weak self] image in
                    DispatchQueue.main.async {
                        self?.overlay = nil
                        self?.openEditor(with: image)
                    }
                },
                onCancel: { [weak self] in
                    DispatchQueue.main.async {
                        self?.overlay = nil
                        self?.showMainWindow(nil)
                    }
                }
            )
            self.overlay = controller
            controller.show()
        }

        // Kendi penceremizin ekrandan kalkması için kısa bir pay bırakılır.
        DispatchQueue.main.asyncAfter(deadline: .now() + delay + 0.2, execute: work)
    }

    @objc private func captureFullScreen() {
        guard ensureScreenPermission() else { return }
        hideOwnWindows()

        let work: () -> Void = { [weak self] in
            guard let self else { return }
            let screen = ScreenCaptureService.screenUnderMouse()
            guard let screenshot = ScreenCaptureService.capture(screen) else {
                self.presentPermissionAlert()
                self.showMainWindow(nil)
                return
            }
            self.openEditor(with: screenshot)
        }

        DispatchQueue.main.asyncAfter(deadline: .now() + delay + 0.2, execute: work)
    }

    private func applyTool(_ tool: EditorTool) {
        defaultTool = tool
        UserDefaults.standard.set(tool.rawValue, forKey: "editorTool")
    }

    private func openEditor(with image: NSImage) {
        guard let controller = EditorWindowController(image: image, tool: defaultTool) else { return }
        controller.onClose = { [weak self] in self?.editor = nil }
        editor = controller
        controller.showWindow(nil)
        controller.window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    // MARK: - İzinler

    private func ensureScreenPermission() -> Bool {
        if ScreenCaptureService.hasPermission() { return true }
        ScreenCaptureService.requestPermission()
        presentPermissionAlert()
        return false
    }

    private func presentPermissionAlert() {
        let alert = NSAlert()
        alert.alertStyle = .informational
        alert.messageText = L("permission.title", "Screen Recording Permission Needed")
        alert.informativeText = L(
            "permission.body",
            """
            SnipFrame needs the Screen Recording permission to take screenshots.

            Open System Settings > Privacy & Security > Screen Recording, enable SnipFrame, \
            then restart the app.
            """
        )
        alert.addButton(withTitle: L("permission.openSettings", "Open System Settings"))
        alert.addButton(withTitle: L("permission.ok", "OK"))

        NSApp.activate(ignoringOtherApps: true)
        if alert.runModal() == .alertFirstButtonReturn {
            ScreenCaptureService.openPrivacySettings()
        }
    }

    private func presentShortcutConflict() {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = L("shortcut.conflict", "That shortcut is already taken by another app.")
        alert.addButton(withTitle: L("permission.ok", "OK"))
        NSApp.activate(ignoringOtherApps: true)
        alert.runModal()
    }

    // MARK: - Menü eylemleri

    @objc private func setDelay(_ sender: NSMenuItem) {
        applyDelay(TimeInterval(sender.tag))
    }

    private func applyDelay(_ seconds: TimeInterval) {
        delay = seconds
        UserDefaults.standard.set(delay, forKey: "captureDelay")
        delayMenuItems.forEach { $0.state = ($0.tag == Int(seconds)) ? .on : .off }
        mainWindow?.setDelay(seconds)
    }

    @objc private func showAbout() {
        let alert = NSAlert()
        alert.messageText = "SnipFrame 1.0"
        alert.informativeText = String(
            format: L(
                "about.body",
                """
                A Snipping Tool style screenshot app for macOS.

                %@ — region snip (change it from the main window)
                Menu bar icon for window / full screen snips.

                In the editor: ⌘C copy, ⌘S save, ⌘Z undo, ⌘⇧Z redo, Esc close.
                """
            ),
            shortcut.displayString
        )
        alert.addButton(withTitle: L("permission.ok", "OK"))
        NSApp.activate(ignoringOtherApps: true)
        alert.runModal()
    }

    @objc private func quit() {
        NSApp.terminate(nil)
    }
}
