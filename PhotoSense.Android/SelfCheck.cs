using Android.App;
using Android.Content;
using Android.Provider;
using Android.Util;

namespace PhotoSense.AndroidApp;

/// <summary>
/// A check the app makes of itself when started with the extra "selfcheck", as GitHub's emulator starts it: the
/// requests the page makes, made from inside the web view, from the phone offered to the groups found, a move,
/// a removal and both undone, and Delete permanently, written down in self-check.json among the app's files on
/// the phone's storage. The photos it works on it first adds to the phone itself, from the samples built into the
/// check's own copy of the app (PhotoSenseSamples), so that it may change them without Android asking the person.
/// </summary>
public static class SelfCheck
{
    private static Context? _context;

    private static int _started;

    public static bool Running { get; private set; }

    /// <summary>
    /// Whether this is the one time the check starts. Android can say a page has finished loading more than once,
    /// and two checks side by side would each find the other's scan already running.
    /// </summary>
    public static bool Starting() => Running && Interlocked.Exchange(ref _started, 1) == 0;

    public static void Prepare(Activity activity, Intent? intent)
    {
        Running = intent?.GetBooleanExtra("selfcheck", false) == true;
        if (!Running) return;
        _context = activity.ApplicationContext;
        // Added once: a second start finds them on the phone already.
        var marker = Path.Combine(activity.FilesDir!.AbsolutePath, "self-check-samples-added");
        if (File.Exists(marker)) return;
        foreach (var name in (activity.Assets!.List("samples") ?? []).Order(StringComparer.Ordinal))
        {
            using var sample = activity.Assets.Open("samples/" + name);
            var bytes = new MemoryStream();
            sample.CopyTo(bytes);
            Add(activity.ContentResolver!, name, bytes.ToArray());
        }
        File.WriteAllText(marker, "");
    }

    private static void Add(ContentResolver resolver, string name, byte[] bytes)
    {
        var video = Path.GetExtension(name).ToLowerInvariant() is ".mp4" or ".mov";
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, name);
        values.Put(MediaStore.IMediaColumns.RelativePath, "DCIM/Camera/");
        values.Put(MediaStore.IMediaColumns.IsPending, 1);
        var collection = video ? MediaStore.Video.Media.GetContentUri(MediaStore.VolumeExternalPrimary)! : MediaStore.Images.Media.GetContentUri(MediaStore.VolumeExternalPrimary)!;
        var uri = resolver.Insert(collection, values) ?? throw new IOException($"{name} could not be added to the phone.");
        using (var output = resolver.OpenOutputStream(uri)!) output.Write(bytes);
        var done = new ContentValues();
        done.Put(MediaStore.IMediaColumns.IsPending, 0);
        resolver.Update(uri, done, null, null);
    }

    // Written after every step, so that a check cut short still says how far it got.
    public static void Write(string report)
    {
        if (_context?.GetExternalFilesDir(null) is not { } folder) return;
        File.WriteAllText(Path.Combine(folder.AbsolutePath, "self-check.json"), report);
        Log.Info("PhotoSense", $"self-check: {report[^Math.Min(300, report.Length)..]}");
    }

    public const string Script = """
        (async () => {
          if (window.photoSenseChecking) return;
          window.photoSenseChecking = true;
          const report = { steps: [], passed: false, finished: false };
          const send = () => PhotoSenseAndroid.report(JSON.stringify(report));
          const step = async (name, run) => {
            const started = Date.now();
            try {
              const value = await run();
              report.steps.push({ name, ok: true, ms: Date.now() - started, value });
              return value;
            } catch (e) {
              report.steps.push({ name, ok: false, ms: Date.now() - started, error: String(e) });
              throw e;
            } finally {
              send();
            }
          };
          // As the page does: Android hands the app no body, so it goes over beside the request.
          const ask = async (path, init) => {
            const carried = init && init.body ? { 'x-photosense-body': PhotoSenseAndroid.keep(init.body) } : {};
            const answer = await fetch('/api/' + path, { ...init, headers: { 'x-photosense-client': 'self-check', 'Content-Type': 'application/json', ...carried } });
            if (!answer.ok) throw new Error(answer.status + ' ' + await answer.text());
            return answer;
          };
          const post = (path, body) => ask(path, { method: 'POST', body: body && JSON.stringify(body) });
          try {
            await step('the page loaded', async () => document.querySelectorAll('script').length);
            await step('health', async () => (await ask('health')).json());
            const root = await step('the phone is offered', async () => (await (await ask('folders')).json()).folders[0].path);
            const camera = root + '/DCIM/Camera';
            await step('a scan starts (a request with a body)', async () => (await post('scan/start', { primaryLocation: root, recursive: true })).json());
            const status = await step('the scan finishes', async () => {
              for (let i = 0; i < 600; i++) {
                const now = await (await ask('scan/status')).json();
                if (now.completed) return now;
                await new Promise(r => setTimeout(r, 500));
              }
              throw new Error('not within five minutes');
            });
            const groups = await step('copies are found', async () => {
              const page = await (await ask('scan/groups?mode=duplicates&page=1&pageSize=50')).json();
              if (page.total < 1) throw new Error('no group of copies');
              return { groups: page.total, keeper: page.items[0].keeper.id, files: page.items[0].members.length + 1 };
            });
            await step('a preview is made', async () => {
              const answer = await ask('photos/' + groups.keeper + '/thumbnail');
              return { type: answer.headers.get('Content-Type'), bytes: (await answer.arrayBuffer()).byteLength };
            });
            await step('a picture is shown whole', async () => {
              const answer = await ask('photos/' + groups.keeper + '/image');
              return { type: answer.headers.get('Content-Type'), bytes: (await answer.arrayBuffer()).byteLength };
            });
            const files = await step('Organize lists the camera folder', async () =>
              (await (await ask('organize/files?root=' + encodeURIComponent(camera))).json()).files);
            const moved = await step('Organize moves a picture into a folder inside DCIM', async () => {
              const picture = files.find(f => !f.isVideo);
              const done = await (await post('organize/apply', { basePath: camera, folderName: 'PhotoSense Check', direct: false, mode: 'move', companions: true,
                label: 'PhotoSense Check', files: [{ id: picture.id, name: picture.name }] })).json();
              if (done.done !== 1) throw new Error(JSON.stringify(done));
              return { batchId: done.batchId, now: done.items[0].path };
            });
            const removed = await step('a copy is removed into _PhotoSense_Removed', async () => {
              const done = await (await post('photos/bulk/remove-duplicates')).json();
              const waiting = await (await ask('removed?root=' + encodeURIComponent(root))).json();
              if (done.removed < 1 || waiting.files < 1) throw new Error(JSON.stringify({ done, waiting }));
              return { batchId: done.batchId, removed: done.removed, waiting: waiting.files, folders: waiting.folders.map(f => f.path) };
            });
            await step('the removal is undone', async () => {
              const undone = await (await post('organize/undo/' + removed.batchId)).json();
              if (undone.restored < 1) throw new Error(JSON.stringify(undone));
              return undone.restored;
            });
            await step('the move is undone', async () => {
              const undone = await (await post('organize/undo/' + moved.batchId)).json();
              if (undone.restored < 1) throw new Error(JSON.stringify(undone));
              return undone.restored;
            });
            await step('a photo is offered to a computer, and no longer once stopped', async () => {
              const offering = await (await post('send/start', { files: [files[0].id] })).json();
              const stopped = await (await post('send/stop')).json();
              if (offering.stage !== 'waiting' || stopped.stage !== 'idle') throw new Error(JSON.stringify({ offering, stopped }));
              return { offering: offering.stage, total: offering.total, phone: offering.phone, stopped: stopped.stage };
            });
            await step('a copy removed again is deleted permanently, into the bin', async () => {
              await post('photos/bulk/remove-duplicates');
              const waiting = await (await ask('removed?root=' + encodeURIComponent(root))).json();
              const erased = await (await post('removed/erase', { folders: waiting.folders.map(f => f.path) })).json();
              if (erased.erased < 1) throw new Error(JSON.stringify({ waiting, erased }));
              return erased;
            });
            report.photos = status.totalPhotos;
            report.passed = true;
          } catch { }
          report.finished = true;
          send();
        })();
        """;
}
