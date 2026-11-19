import AppKit
import Carbon.HIToolbox

/// Global kısayol modeli (Carbon anahtar kodu + değiştirici bayrakları).
struct Shortcut: Equatable {

    var keyCode: UInt32
    var carbonModifiers: UInt32

    static let `default` = Shortcut(keyCode: UInt32(kVK_ANSI_X), carbonModifiers: UInt32(cmdKey | shiftKey))

    var displayString: String {
        var parts: [String] = []
        if carbonModifiers & UInt32(controlKey) != 0 { parts.append("⌃") }
        if carbonModifiers & UInt32(optionKey) != 0 { parts.append("⌥") }
        if carbonModifiers & UInt32(shiftKey) != 0 { parts.append("⇧") }
        if carbonModifiers & UInt32(cmdKey) != 0 { parts.append("⌘") }
        return parts.joined() + Shortcut.keyName(for: keyCode)
    }

    /// ⌘/⌥/⌃ içermeyen (ve fonksiyon tuşu olmayan) kombinasyonlar kabul edilmez.
    static func from(event: NSEvent) -> Shortcut? {
        let flags = event.modifierFlags.intersection([.command, .option, .control, .shift])
        var mods: UInt32 = 0
        if flags.contains(.command) { mods |= UInt32(cmdKey) }
        if flags.contains(.option) { mods |= UInt32(optionKey) }
        if flags.contains(.control) { mods |= UInt32(controlKey) }
        if flags.contains(.shift) { mods |= UInt32(shiftKey) }

        let keyCode = UInt32(event.keyCode)
        let hasCommandModifier = mods & UInt32(cmdKey | optionKey | controlKey) != 0
        guard hasCommandModifier || functionKeyCodes.contains(keyCode) else { return nil }
        return Shortcut(keyCode: keyCode, carbonModifiers: mods)
    }

    // MARK: - Tuş adları

    private static let functionKeyCodes: Set<UInt32> = [
        122, 120, 99, 118, 96, 97, 98, 100, 101, 109, 103, 111,
        105, 107, 113, 106, 64, 79, 80, 90
    ]

    private static let specialKeyNames: [UInt32: String] = [
        49: "Space", 36: "↩", 76: "⌤", 48: "⇥", 51: "⌫", 117: "⌦", 53: "⎋",
        123: "←", 124: "→", 125: "↓", 126: "↑",
        115: "↖", 119: "↘", 116: "⇞", 121: "⇟",
        122: "F1", 120: "F2", 99: "F3", 118: "F4", 96: "F5", 97: "F6",
        98: "F7", 100: "F8", 101: "F9", 109: "F10", 103: "F11", 111: "F12",
        105: "F13", 107: "F14", 113: "F15", 106: "F16", 64: "F17", 79: "F18",
        80: "F19", 90: "F20"
    ]

    static func keyName(for keyCode: UInt32) -> String {
        if let special = specialKeyNames[keyCode] { return special }
        if let character = character(for: keyCode), !character.isEmpty {
            return character.uppercased()
        }
        return "Key \(keyCode)"
    }

    /// Geçerli klavye düzenine göre tuşun karakterini bulur.
    private static func character(for keyCode: UInt32) -> String? {
        guard let source = TISCopyCurrentKeyboardLayoutInputSource()?.takeRetainedValue(),
              let layoutPointer = TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData)
        else { return nil }

        let layoutData = Unmanaged<CFData>.fromOpaque(layoutPointer).takeUnretainedValue() as Data
        return layoutData.withUnsafeBytes { raw -> String? in
            guard let layout = raw.baseAddress?.assumingMemoryBound(to: UCKeyboardLayout.self) else { return nil }
            var deadKeyState: UInt32 = 0
            var characters = [UniChar](repeating: 0, count: 4)
            var length = 0
            let status = UCKeyTranslate(
                layout,
                UInt16(keyCode),
                UInt16(kUCKeyActionDisplay),
                0,
                UInt32(LMGetKbdType()),
                OptionBits(kUCKeyTranslateNoDeadKeysBit),
                &deadKeyState,
                characters.count,
                &length,
                &characters
            )
            guard status == noErr, length > 0 else { return nil }
            return String(utf16CodeUnits: characters, count: length)
        }
    }
}

/// Kısayolun UserDefaults'ta saklanması.
enum ShortcutStore {

    private static let keyCodeKey = "hotkeyKeyCode"
    private static let modifiersKey = "hotkeyModifiers"

    static var current: Shortcut {
        let defaults = UserDefaults.standard
        guard defaults.object(forKey: keyCodeKey) != nil else { return .default }
        return Shortcut(
            keyCode: UInt32(defaults.integer(forKey: keyCodeKey)),
            carbonModifiers: UInt32(defaults.integer(forKey: modifiersKey))
        )
    }

    static func save(_ shortcut: Shortcut) {
        let defaults = UserDefaults.standard
        defaults.set(Int(shortcut.keyCode), forKey: keyCodeKey)
        defaults.set(Int(shortcut.carbonModifiers), forKey: modifiersKey)
    }

    static func reset() {
        let defaults = UserDefaults.standard
        defaults.removeObject(forKey: keyCodeKey)
        defaults.removeObject(forKey: modifiersKey)
    }
}
