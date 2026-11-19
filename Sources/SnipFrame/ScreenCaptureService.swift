import AppKit
import CoreGraphics

enum ScreenCaptureService {

    static func hasPermission() -> Bool {
        CGPreflightScreenCaptureAccess()
    }

    @discardableResult
    static func requestPermission() -> Bool {
        CGRequestScreenCaptureAccess()
    }

    static func displayID(of screen: NSScreen) -> CGDirectDisplayID? {
        (screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value
    }

    /// Captures a single display at native (Retina) pixel resolution.
    static func capture(_ screen: NSScreen) -> NSImage? {
        guard let displayID = displayID(of: screen),
              let cgImage = CGDisplayCreateImage(displayID) else { return nil }
        return NSImage(cgImage: cgImage, size: screen.frame.size)
    }

    static func screenUnderMouse() -> NSScreen {
        let location = NSEvent.mouseLocation
        return NSScreen.screens.first { NSMouseInRect(location, $0.frame, false) }
            ?? NSScreen.main
            ?? NSScreen.screens[0]
    }

    static func openPrivacySettings() {
        let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture")!
        NSWorkspace.shared.open(url)
    }
}
