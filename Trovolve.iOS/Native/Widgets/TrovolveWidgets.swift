// The home screen widgets on an iPhone or iPad (35): Storage and Swipe to clean. A widget runs in a process of its
// own and cannot ask Trovolve anything, so the app writes what to show into the App Group both share, and asks
// WidgetKit to draw them again. A tap opens Trovolve with a trovolve:// link saying what to open.
import SwiftUI
import WidgetKit

/// What the widgets show, as the app writes it (Trovolve.Domain.Services.WidgetSnapshot).
struct Snapshot: Decodable {
    let PercentFull: Int
    let Freeable: String
    let BackupLine: String
    let BackupButton: String
    let Ready: Int
    let ReadyLine: String
    let Week: [Bool]
    let StreakLine: String
}

enum Shared {
    static let group = "group.com.j2k2ham.trovolve"

    /// What the app last wrote; nil before it has written anything.
    static func load() -> Snapshot? {
        guard let url = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: group)?.appendingPathComponent("widgets.json"),
              let data = try? Data(contentsOf: url) else { return nil }
        return try? JSONDecoder().decode(Snapshot.self, from: data)
    }
}

struct Entry: TimelineEntry {
    let date: Date
    let snapshot: Snapshot?
}

/// One entry, drawn again only when the app asks: what it shows changes only as Trovolve learns more.
struct Provider: TimelineProvider {
    func placeholder(in context: Context) -> Entry { Entry(date: Date(), snapshot: nil) }

    func getSnapshot(in context: Context, completion: @escaping (Entry) -> Void) {
        completion(Entry(date: Date(), snapshot: Shared.load()))
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<Entry>) -> Void) {
        completion(Timeline(entries: [Entry(date: Date(), snapshot: Shared.load())], policy: .never))
    }
}

/// The page's accent, oklch(0.76 0.11 205) in the dark and oklch(0.50 0.10 215) in the light.
let accent = Color(UIColor { $0.userInterfaceStyle == .dark ? UIColor(red: 0.29, green: 0.76, blue: 0.83, alpha: 1) : UIColor(red: 0.0, green: 0.42, blue: 0.50, alpha: 1) })
let onAccent = Color(UIColor { $0.userInterfaceStyle == .dark ? UIColor(red: 0.0, green: 0.14, blue: 0.17, alpha: 1) : .white })
let amber = Color(UIColor { $0.userInterfaceStyle == .dark ? UIColor(red: 1.0, green: 0.84, blue: 0.04, alpha: 1) : UIColor(red: 0.54, green: 0.35, blue: 0.0, alpha: 1) })

extension View {
    /// The widget's background: the system's own on iOS 17, as it asks; a plain one before.
    @ViewBuilder func widgetBackground() -> some View {
        if #available(iOSApplicationExtension 17.0, *) {
            containerBackground(for: .widget) { Color(.secondarySystemBackground) }
        } else {
            padding().background(Color(.secondarySystemBackground))
        }
    }
}

/// The brand's Swipe to clean: a card tipped off another, the one behind lighter.
struct SwipeMark: View {
    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 2.5 * 18 / 24).fill(accent.opacity(0.35))
                .frame(width: 11 * 18 / 24, height: 15 * 18 / 24).rotationEffect(.degrees(-12)).offset(x: -2.5 * 18 / 24, y: 0.5 * 18 / 24)
            RoundedRectangle(cornerRadius: 2.5 * 18 / 24).fill(accent)
                .frame(width: 11 * 18 / 24, height: 15 * 18 / 24).rotationEffect(.degrees(10)).offset(x: 2.5 * 18 / 24, y: -0.5 * 18 / 24)
        }
        .frame(width: 18, height: 18)
        .accessibilityHidden(true)
    }
}

/// Trovolve's Free up space icon, drawn the same as branding/svg/feature-icons/storage.svg:
/// on the brand's 24-unit grid, in one colour with lighter layers.
/// The colour comes from the foreground style, e.g. `.foregroundStyle(.tint)`.
struct FreeUpSpaceIcon: View {
    var body: some View {
        ZStack {
            FreeUpSpaceShape(part: .track).opacity(0.3)
            FreeUpSpaceShape(part: .used)
            FreeUpSpaceShape(part: .sparkle).opacity(0.6)
        }
        .aspectRatio(1, contentMode: .fit)
        .accessibilityHidden(true)
    }
}

/// One layer of the Free up space icon, drawn in 24-unit grid coordinates and scaled to fit, centred.
private struct FreeUpSpaceShape: Shape {
    enum Part { case track, used, sparkle }

    let part: Part

    func path(in rect: CGRect) -> Path {
        var p = Path()
        let corner = CGSize(width: 2.5, height: 2.5)
        switch part {
        case .track:
            // The whole storage bar.
            p.addRoundedRect(in: CGRect(x: 3, y: 6, width: 18, height: 12), cornerSize: corner, style: .circular)
        case .used:
            // The space still used, flush with the bar's left end.
            p.addRoundedRect(in: CGRect(x: 3, y: 6, width: 7, height: 12), cornerSize: corner, style: .circular)
        case .sparkle:
            // A four-point star in the space that was freed, the same curve as the brand's stars.
            let cx = 16.0, cy = 12.0, r = 3.4
            let a = 0.14 * r, b = 0.28 * r
            p.move(to: CGPoint(x: cx, y: cy - r))
            p.addCurve(to: CGPoint(x: cx + r, y: cy), control1: CGPoint(x: cx + a, y: cy - b), control2: CGPoint(x: cx + b, y: cy - a))
            p.addCurve(to: CGPoint(x: cx, y: cy + r), control1: CGPoint(x: cx + b, y: cy + a), control2: CGPoint(x: cx + a, y: cy + b))
            p.addCurve(to: CGPoint(x: cx - r, y: cy), control1: CGPoint(x: cx - a, y: cy + b), control2: CGPoint(x: cx - b, y: cy + a))
            p.addCurve(to: CGPoint(x: cx, y: cy - r), control1: CGPoint(x: cx - b, y: cy - a), control2: CGPoint(x: cx - a, y: cy - b))
            p.closeSubpath()
        }
        let scale = min(rect.width, rect.height) / 24
        let transform = CGAffineTransform(translationX: rect.midX - 12 * scale, y: rect.midY - 12 * scale)
            .scaledBy(x: scale, y: scale)
        return p.applying(transform)
    }
}

/// 35: how full the phone is, what can be freed, and backing up, with Back up now.
struct StorageView: View {
    let entry: Entry

    var body: some View {
        let s = entry.snapshot
        VStack(alignment: .leading, spacing: 2) {
            HStack {
                Text("Trovolve").font(.caption).foregroundStyle(.secondary)
                Spacer()
                if let s { Text("\(s.PercentFull)% full").font(.caption.bold()).foregroundStyle(s.PercentFull >= 90 ? amber : .secondary) }
            }
            Spacer(minLength: 4)
            Text(s?.Freeable ?? "Trovolve").font(.system(size: 28, weight: .bold)).minimumScaleFactor(0.6).lineLimit(1)
            HStack(spacing: 5) {
                FreeUpSpaceIcon()
                    .foregroundStyle(accent)
                    .frame(width: 24, height: 24)
                Text("can be freed").font(.caption).foregroundStyle(.secondary)
            }
            Spacer(minLength: 4)
            HStack {
                Text(s?.BackupLine ?? "Open Trovolve to start").font(.caption).foregroundStyle(.secondary).lineLimit(1)
                Spacer()
                Link(destination: URL(string: "trovolve://opens/backup")!) {
                    Text(s?.BackupButton ?? "Back up now").font(.subheadline.bold()).padding(.horizontal, 12).padding(.vertical, 6)
                        .background(accent, in: Capsule()).foregroundStyle(onAccent)
                }
            }
        }
        .widgetURL(URL(string: "trovolve://opens/free-up-space"))
        .widgetBackground()
    }
}

/// 35: how many of today's set to swipe are left, the week's days, and the streak.
struct SwipeView: View {
    let entry: Entry

    var body: some View {
        let s = entry.snapshot
        VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: 5) {
                SwipeMark()
                Text("Swipe to clean").font(.caption.bold()).foregroundStyle(.secondary)
            }
            Text(s.map { "\($0.Ready)" } ?? "–").font(.system(size: 34, weight: .bold))
            Text(s?.ReadyLine ?? "Open Trovolve to start").font(.caption).foregroundStyle(.secondary).lineLimit(2)
            Spacer(minLength: 4)
            HStack(spacing: 4) {
                ForEach(Array((s?.Week ?? []).enumerated()), id: \.offset) { _, swiped in
                    Circle().fill(swiped ? accent : Color.clear).overlay(Circle().stroke(accent, lineWidth: 1.5)).frame(width: 9, height: 9)
                }
            }
            Text(s?.StreakLine ?? "").font(.caption.bold()).lineLimit(1)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .widgetURL(URL(string: "trovolve://opens/swipe"))
        .widgetBackground()
    }
}

struct StorageWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "Storage", provider: Provider()) { StorageView(entry: $0) }
            .configurationDisplayName("Storage")
            .description("How full your device is, what can be freed, and backing up.")
            .supportedFamilies([.systemMedium])
    }
}

struct SwipeWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "Swipe", provider: Provider()) { SwipeView(entry: $0) }
            .configurationDisplayName("Swipe to clean")
            .description("Today's set to swipe, and your streak.")
            .supportedFamilies([.systemSmall])
    }
}

@main
struct TrovolveWidgets: WidgetBundle {
    var body: some Widget {
        StorageWidget()
        SwipeWidget()
    }
}
