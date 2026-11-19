// WidgetKit for PhotoSense's C# code, which cannot call Swift itself: asks the home screen widgets to be drawn again,
// once the app has written what they show into the App Group they share.

import WidgetKit

@_cdecl("photosense_widgets_reload")
public func photosense_widgets_reload() {
    WidgetCenter.shared.reloadAllTimelines()
}
