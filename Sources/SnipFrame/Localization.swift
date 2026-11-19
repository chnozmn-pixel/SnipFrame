import Foundation

/// Yerelleştirme: metin anahtarı + İngilizce varsayılan. Sistem dili neyse o .lproj kullanılır.
func L(_ key: String, _ fallback: String) -> String {
    NSLocalizedString(key, tableName: nil, bundle: .main, value: fallback, comment: "")
}
