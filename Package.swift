// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "SnipFrame",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(
            name: "SnipFrame",
            path: "Sources/SnipFrame"
        )
    ]
)
