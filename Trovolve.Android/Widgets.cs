using System.Text.Json;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;
using Trovolve.Domain.Services;

namespace Trovolve.AndroidApp;

/// <summary>
/// The home screen widgets on Android (35), drawn with Android's own <see cref="RemoteViews"/>. What they show is kept
/// in a file of the app's own, so that a widget added later, or drawn again by Android, shows it at once.
/// </summary>
internal sealed class AndroidWidgets(Context context) : IWidgets
{
    private string Kept => Path.Combine(context.FilesDir!.AbsolutePath, "widgets.json");

    /// <inheritdoc />
    public void Show(WidgetSnapshot snapshot)
    {
        File.WriteAllText(Kept, JsonSerializer.Serialize(snapshot));
        Draw(context, snapshot);
    }

    /// <summary>What the widgets last showed; null before Trovolve has handed them anything.</summary>
    public static WidgetSnapshot? Last(Context context)
    {
        var kept = Path.Combine(context.FilesDir!.AbsolutePath, "widgets.json");
        return File.Exists(kept) ? JsonSerializer.Deserialize<WidgetSnapshot>(File.ReadAllText(kept)) : null;
    }

    /// <summary>Draws every Trovolve widget on the home screen with what to show.</summary>
    public static void Draw(Context context, WidgetSnapshot? snapshot)
    {
        var widgets = AppWidgetManager.GetInstance(context)!;
        widgets.UpdateAppWidget(new ComponentName(context, Java.Lang.Class.FromType(typeof(StorageWidget))), StorageWidget.Views(context, snapshot));
        widgets.UpdateAppWidget(new ComponentName(context, Java.Lang.Class.FromType(typeof(SwipeWidget))), SwipeWidget.Views(context, snapshot));
    }

    /// <summary>Opens Trovolve saying what to open, as a tapped notification does.</summary>
    public static PendingIntent Opening(Context context, string opens, int request)
    {
        var intent = new Intent(context, typeof(MainActivity)).AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop)!.PutExtra(AndroidNotifier.OpensExtra, opens)!;
        return PendingIntent.GetActivity(context, request, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }
}

/// <summary>35: the Storage widget: how full the phone is, what can be freed, and backing up, with Back up now.</summary>
[BroadcastReceiver(Name = "com.j2k2ham.trovolve.StorageWidget", Label = "@string/widget_storage", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/widget_storage_info")]
public sealed class StorageWidget : AppWidgetProvider
{
    /// <summary>Android asks for the widget to be drawn: with what Trovolve last handed it.</summary>
    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context is not null) appWidgetManager?.UpdateAppWidget(appWidgetIds, Views(context, AndroidWidgets.Last(context)));
    }

    /// <summary>The widget as it is drawn; before Trovolve has said anything, an invitation to open it.</summary>
    public static RemoteViews Views(Context context, WidgetSnapshot? snapshot)
    {
        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_storage);
        views.SetTextViewText(Resource.Id.storage_full, snapshot is null ? "" : $"{snapshot.PercentFull}% full");
        // Amber once the phone is 90% full, as on an iPhone; quiet before.
        views.SetTextColor(Resource.Id.storage_full, new Android.Graphics.Color(context.GetColor(snapshot?.PercentFull >= 90 ? Resource.Color.widget_amber : Resource.Color.widget_faint)));
        views.SetTextViewText(Resource.Id.storage_freeable, snapshot?.Freeable ?? "Trovolve");
        views.SetTextViewText(Resource.Id.storage_backup, snapshot?.BackupLine ?? "Open Trovolve to start");
        views.SetTextViewText(Resource.Id.storage_button, snapshot?.BackupButton ?? "Back up now");
        views.SetOnClickPendingIntent(Resource.Id.widget_storage, AndroidWidgets.Opening(context, "free-up-space", 41));
        views.SetOnClickPendingIntent(Resource.Id.storage_button, AndroidWidgets.Opening(context, "backup", 42));
        return views;
    }
}

/// <summary>35: the Swipe to clean widget: how many of today's set are left, the week, and the streak.</summary>
[BroadcastReceiver(Name = "com.j2k2ham.trovolve.SwipeWidget", Label = "@string/widget_swipe", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/widget_swipe_info")]
public sealed class SwipeWidget : AppWidgetProvider
{
    /// <summary>Android asks for the widget to be drawn: with what Trovolve last handed it.</summary>
    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context is not null) appWidgetManager?.UpdateAppWidget(appWidgetIds, Views(context, AndroidWidgets.Last(context)));
    }

    /// <summary>The widget as it is drawn: the week's days as dots, filled where something was swiped.</summary>
    public static RemoteViews Views(Context context, WidgetSnapshot? snapshot)
    {
        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_swipe);
        views.SetTextViewText(Resource.Id.swipe_ready, snapshot is null ? "–" : snapshot.Ready.ToString("N0"));
        views.SetTextViewText(Resource.Id.swipe_ready_line, snapshot?.ReadyLine ?? "Open Trovolve to start");
        views.SetTextViewText(Resource.Id.swipe_week, string.Join(' ', (snapshot?.Week ?? []).Select(d => d ? '●' : '○')));
        views.SetTextViewText(Resource.Id.swipe_streak, snapshot?.StreakLine ?? "");
        views.SetOnClickPendingIntent(Resource.Id.widget_swipe, AndroidWidgets.Opening(context, "swipe", 43));
        return views;
    }
}
