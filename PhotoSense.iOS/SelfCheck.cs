using WebKit;

namespace PhotoSense.iOS;

/// <summary>
/// A check the app makes of itself when started with PHOTOSENSE_SELF_CHECK=1, as GitHub's Mac starts it in the
/// simulator: the requests the page makes, made from inside the web view, from the library offered to the groups
/// found and the previews made, written down in self-check.json among the app's own files.
/// </summary>
public static class SelfCheck
{
    public static void AddTo(WKWebViewConfiguration config)
    {
        if (Environment.GetEnvironmentVariable("PHOTOSENSE_SELF_CHECK") != "1") return;
        config.UserContentController.AddScriptMessageHandler(new Report(), "selfCheck");
        config.UserContentController.AddUserScript(new WKUserScript(new NSString(Script), WKUserScriptInjectionTime.AtDocumentEnd, true));
    }

    private sealed class Report : NSObject, IWKScriptMessageHandler
    {
        // Written after every step, so that a check cut short still says how far it got.
        [Export("userContentController:didReceiveScriptMessage:")]
        public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
        {
            var report = $"{message.Body}";
            File.WriteAllText(Path.Combine(PhoneApp.DataFolder, "self-check.json"), report);
            Say.Line($"self-check: {report[^Math.Min(300, report.Length)..]}");
        }
    }

    private const string Script = """
        (async () => {
          const headers = { 'x-photosense-client': 'self-check', 'Content-Type': 'application/json' };
          const report = { steps: [], passed: false, finished: false };
          const send = () => window.webkit.messageHandlers.selfCheck.postMessage(JSON.stringify(report));
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
          const ask = async (path, init) => {
            const answer = await fetch('/api/' + path, { headers, ...init });
            if (!answer.ok) throw new Error(answer.status + ' ' + await answer.text());
            return answer;
          };
          try {
            await step('the page loaded', async () => document.querySelectorAll('script').length);
            await step('health', async () => (await ask('health')).json());
            const root = await step('the library is offered', async () => (await (await ask('folders')).json()).folders[0].path);
            await step('a scan starts (a request with a body)', async () =>
              (await ask('scan/start', { method: 'POST', body: JSON.stringify({ primaryLocation: root, recursive: true }) })).json());
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
            await step('Organize lists the library', async () =>
              (await (await ask('organize/files?root=' + encodeURIComponent(root))).json()).files.length);
            await step('a removal is refused', async () => {
              const answer = await fetch('/api/photos/bulk/remove-duplicates', { method: 'POST', headers });
              const said = await answer.text();
              if (!said.includes('cannot change the photos on this phone')) throw new Error(answer.status + ' ' + said);
              return answer.status;
            });
            report.photos = status.totalPhotos;
            report.passed = true;
          } catch { }
          report.finished = true;
          send();
        })();
        """;
}
