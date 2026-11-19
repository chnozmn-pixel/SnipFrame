import AppKit
import Carbon.HIToolbox

/// Global kısayolu Accessibility izni gerektirmeden kaydeder.
final class HotkeyManager {
    static let shared = HotkeyManager()

    private var hotKeyRef: EventHotKeyRef?
    private var eventHandler: EventHandlerRef?
    private var action: (() -> Void)?
    private let signature: OSType = 0x4B415245 // 'KARE'

    private init() {}

    /// Kayıt başarılıysa `true` döner; başka bir uygulama kısayolu kapmışsa `false`.
    @discardableResult
    func register(_ shortcut: Shortcut, action: @escaping () -> Void) -> Bool {
        unregister()
        self.action = action

        var eventType = EventTypeSpec(
            eventClass: OSType(kEventClassKeyboard),
            eventKind: UInt32(kEventHotKeyPressed)
        )

        let pointer = UnsafeMutableRawPointer(Unmanaged.passUnretained(self).toOpaque())
        InstallEventHandler(
            GetApplicationEventTarget(),
            { _, _, userData -> OSStatus in
                guard let userData else { return noErr }
                let manager = Unmanaged<HotkeyManager>.fromOpaque(userData).takeUnretainedValue()
                manager.action?()
                return noErr
            },
            1,
            &eventType,
            pointer,
            &eventHandler
        )

        let hotKeyID = EventHotKeyID(signature: signature, id: 1)
        var reference: EventHotKeyRef?
        let status = RegisterEventHotKey(
            shortcut.keyCode,
            shortcut.carbonModifiers,
            hotKeyID,
            GetApplicationEventTarget(),
            0,
            &reference
        )

        guard status == noErr, let reference else {
            if let eventHandler {
                RemoveEventHandler(eventHandler)
                self.eventHandler = nil
            }
            self.action = nil
            return false
        }

        hotKeyRef = reference
        return true
    }

    func unregister() {
        if let hotKeyRef {
            UnregisterEventHotKey(hotKeyRef)
            self.hotKeyRef = nil
        }
        if let eventHandler {
            RemoveEventHandler(eventHandler)
            self.eventHandler = nil
        }
        action = nil
    }
}
