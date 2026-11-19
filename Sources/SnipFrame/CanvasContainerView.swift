import AppKit

/// Flipped host view so an unflipped image canvas starts at the top-left of a scroll view.
final class CanvasContainerView: NSView {
    override var isFlipped: Bool { true }
}
