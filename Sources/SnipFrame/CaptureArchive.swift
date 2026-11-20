import AppKit

/// Her yakalama, tarihli bir PNG olarak Arşiv klasörüne de yazılır; böylece editör kapandıktan
/// sonra da görüntü bulunabilir.
enum CaptureArchive {

    static var folderURL: URL {
        let pictures = FileManager.default.urls(for: .picturesDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Pictures")
        return pictures.appendingPathComponent("SnipFrame", isDirectory: true)
    }

    static var folderPath: String { folderURL.path }

    /// Görüntüyü klasöre yazar; başarılıysa dosya adresini döner.
    @discardableResult
    static func save(_ image: NSImage) -> URL? {
        guard let cgImage = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return nil }
        return save(cgImage)
    }

    @discardableResult
    static func save(_ cgImage: CGImage) -> URL? {
        let rep = NSBitmapImageRep(cgImage: cgImage)
        guard let data = rep.representation(using: .png, properties: [:]) else { return nil }

        do {
            try FileManager.default.createDirectory(at: folderURL, withIntermediateDirectories: true)
            let url = uniqueURL()
            try data.write(to: url)
            return url
        } catch {
            // Arşivleme bir kolaylık: hata yakalamayı kesmemeli.
            return nil
        }
    }

    static func openFolder() {
        try? FileManager.default.createDirectory(at: folderURL, withIntermediateDirectories: true)
        NSWorkspace.shared.open(folderURL)
    }

    private static func uniqueURL() -> URL {
        let formatter = DateFormatter()
        formatter.dateFormat = "yyyy-MM-dd HH-mm-ss"
        let base = "SnipFrame \(formatter.string(from: Date()))"

        var url = folderURL.appendingPathComponent("\(base).png")
        var index = 2
        while FileManager.default.fileExists(atPath: url.path) {
            url = folderURL.appendingPathComponent("\(base) (\(index)).png")
            index += 1
        }
        return url
    }
}
