import { beforeEach, describe, expect, it, vi } from 'vitest';
import { deferred } from '../fakeSignalR';
import { reply } from '../fixtures';

const hub = await vi.hoisted(async () => new (await import('../fakeSignalR')).FakeHub());
const swr = vi.hoisted(() => ({ useSWR: vi.fn(), mutate: vi.fn() }));
vi.mock('@microsoft/signalr', () => hub.module);
vi.mock('swr', () => ({ default: swr.useSWR, mutate: swr.mutate }));

const API = 'http://localhost:7071/api';
const fetchMock = vi.fn<typeof fetch>();

beforeEach(() => {
  hub.reset();
  swr.useSWR.mockReset();
  swr.mutate.mockReset();
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});

// The service's address and key are read once, when the module loads.
async function load(env: { base?: string; key?: string } = {}) {
  vi.resetModules();
  vi.stubEnv('NEXT_PUBLIC_API_BASE', env.base);
  vi.stubEnv('NEXT_PUBLIC_API_KEY', env.key);
  return import('../../lib/apiClient');
}

function lastRequest() {
  const [url, init] = fetchMock.mock.calls[fetchMock.mock.calls.length - 1];
  return { url: String(url), init };
}

const answering = (body?: unknown, status = 200) => fetchMock.mockImplementation(async () => reply(body, status));

describe('addresses', () => {
  it('point at the local service unless told otherwise', async () => {
    const api = await load();
    expect(api.thumbnailUrl('abc')).toBe(`${API}/photos/abc/thumbnail`);
    expect(api.imageUrl('abc')).toBe(`${API}/photos/abc/image`);
    expect(api.videoUrl('abc')).toBe(`${API}/photos/abc/video`);
  });

  it('follow NEXT_PUBLIC_API_BASE when it is set', async () => {
    const api = await load({ base: 'https://photos.example/api' });
    expect(api.imageUrl('abc')).toBe('https://photos.example/api/photos/abc/image');
  });
});

describe('requests that act on photos', () => {
  it('carry the header the service insists on', async () => {
    answering(undefined, 204);
    const api = await load();
    await api.openInViewer('abc');
    expect(lastRequest()).toEqual({ url: `${API}/photos/abc/open`, init: { method: 'POST', headers: { 'x-photosense-client': 'web' } } });
  });

  it('carry the API key too when one is configured', async () => {
    answering(undefined, 204);
    const api = await load({ key: 'secret-1' });
    await api.openInViewer('abc');
    expect(lastRequest().init?.headers).toEqual({ 'x-photosense-client': 'web', 'x-api-key': 'secret-1' });
  });

  it('mark a copy to keep, or clear the mark, and refresh the groups', async () => {
    answering(undefined, 204);
    const api = await load();

    await api.setKept('abc', true);
    expect(lastRequest()).toEqual({ url: `${API}/photos/abc/keep?kept=true`, init: { method: 'POST', headers: { 'x-photosense-client': 'web' } } });
    await api.setKept('abc', false);
    expect(lastRequest().url).toBe(`${API}/photos/abc/keep?kept=false`);

    expect(swr.mutate).toHaveBeenCalledTimes(2);
    // Only the group listings are refreshed, whatever else is cached.
    const isGroupListing = swr.mutate.mock.calls[0][0] as (key: unknown) => boolean;
    expect(isGroupListing(`${API}/scan/groups?mode=duplicates&page=1&hideKept=false&q=`)).toBe(true);
    expect(isGroupListing(`${API}/scan/progress/scan-1`)).toBe(false);
    expect(isGroupListing(['/scan/groups'])).toBe(false);
    expect(isGroupListing(null)).toBe(false);
  });

  it('remove one file and say how many linked files went with it', async () => {
    answering({ companions: 2 });
    const api = await load();
    await expect(api.removePhoto('abc')).resolves.toEqual({ companions: 2 });
    expect(lastRequest()).toEqual({ url: `${API}/photos/abc?physical=true`, init: { method: 'DELETE', headers: { 'x-photosense-client': 'web' } } });
    expect(swr.mutate).toHaveBeenCalledOnce();
  });

  it('remove the duplicates of one group, or of every group', async () => {
    const result = { removed: 3, bytes: 9_000_000, skipped: 0, companions: 1, problems: [] };
    answering(result);
    const api = await load();

    await expect(api.removeDuplicates('group 1/á')).resolves.toEqual(result);
    expect(lastRequest()).toEqual({ url: `${API}/photos/bulk/remove-duplicates?group=group%201%2F%C3%A1`, init: { method: 'POST', headers: { 'x-photosense-client': 'web' } } });
    await api.removeDuplicates();
    expect(lastRequest().url).toBe(`${API}/photos/bulk/remove-duplicates`);
    await api.removeDuplicates('');
    expect(lastRequest().url).toBe(`${API}/photos/bulk/remove-duplicates`);
    expect(swr.mutate).toHaveBeenCalledTimes(3);
  });

  it('start a scan and hand back its id', async () => {
    answering({ instanceId: 'scan-7' });
    const api = await load();
    const request = { primaryLocation: 'C:\\photos', secondaryLocation: 'D:\\backup', recursive: true };
    await expect(api.startScan(request)).resolves.toEqual({ instanceId: 'scan-7' });
    expect(lastRequest()).toEqual({
      url: `${API}/scan/start`,
      init: { method: 'POST', body: JSON.stringify(request), headers: { 'Content-Type': 'application/json', 'x-photosense-client': 'web' } },
    });
  });

  it.each([
    ['{"error":"The file is no longer there. Scan again."}', 404, 'The file is no longer there. Scan again.'],
    ['{"detail":"not the usual shape"}', 500, '{"detail":"not the usual shape"}'],
    ['Folder not found: C:\\nowhere', 400, 'Folder not found: C:\\nowhere'],
    ['', 503, 'Request failed (503)'],
  ])('report a refusal %j with status %d as "%s"', async (body, status, message) => {
    answering(body, status);
    const api = await load();
    await expect(api.openInViewer('abc')).rejects.toMatchObject({ message });
    await expect(api.removePhoto('abc')).rejects.toMatchObject({ message });
    await expect(api.startScan({ primaryLocation: 'C:\\nowhere', recursive: true })).rejects.toMatchObject({ message });
    // Nothing changed, so nothing is refreshed.
    expect(swr.mutate).not.toHaveBeenCalled();
  });
});

describe('what the page reads', () => {
  it('asks for groups by mode, page, search text and whether reviewed ones are hidden', async () => {
    swr.useSWR.mockReturnValue({ data: 'the groups' });
    const api = await load();

    expect(api.useGroups('duplicates', '', 1, false)).toEqual({ data: 'the groups' });
    expect(swr.useSWR).toHaveBeenLastCalledWith(`${API}/scan/groups?mode=duplicates&page=1&hideKept=false&q=`, expect.any(Function), { refreshInterval: 5000, keepPreviousData: true });

    api.useGroups('similar', 'trip & beach', 3, true);
    expect(swr.useSWR.mock.calls[1][0]).toBe(`${API}/scan/groups?mode=similar&page=3&hideKept=true&q=trip%20%26%20beach`);
  });

  it('follows a scan only once one has been started', async () => {
    swr.useSWR.mockReturnValue({ data: 'progress' });
    const api = await load();

    expect(api.useScanProgress()).toEqual({ data: 'progress' });
    expect(swr.useSWR).toHaveBeenLastCalledWith(null, expect.any(Function), { refreshInterval: 1500 });
    api.useScanProgress('scan-7');
    expect(swr.useSWR.mock.calls[1][0]).toBe(`${API}/scan/progress/scan-7`);
  });

  it('is fetched as JSON, and a refusal is reported in the service\'s words', async () => {
    const api = await load();
    api.useGroups('duplicates', '', 1, false);
    const [key, fetcher] = swr.useSWR.mock.calls[0] as [string, (url: string) => Promise<unknown>];

    answering({ mode: 'duplicates', items: [] });
    await expect(fetcher(key)).resolves.toEqual({ mode: 'duplicates', items: [] });
    expect(lastRequest()).toEqual({ url: key, init: { headers: { 'Content-Type': 'application/json', 'x-photosense-client': 'web' } } });

    answering('The database is busy', 500);
    await expect(fetcher(key)).rejects.toMatchObject({ message: 'The database is busy' });
  });
});

describe('the log stream', () => {
  const negotiate = `${API}/scan/logs/negotiate`;
  const polled = `${API}/scan/logs?limit=200`;
  const hubOffer = { url: 'https://hub.example/client', accessToken: 'token-1' };
  const settle = () => vi.advanceTimersByTimeAsync(0);
  const line = (n: number) => ({ timestamp: `12:00:0${n}`, level: 'Info', message: `line ${n}` });

  beforeEach(() => { vi.useFakeTimers(); });

  it('arrives over the hub when the service offers one', async () => {
    answering(hubOffer);
    const api = await load();
    const lines: string[] = [];

    const dispose = api.connectLogStream(l => lines.push(l));
    await settle();

    expect(lastRequest()).toEqual({ url: negotiate, init: { method: 'POST' } });
    expect(hub.connection.url).toBe('https://hub.example/client');
    expect(hub.connection.options.accessTokenFactory()).toBe('token-1');
    expect(hub.connection.start).toHaveBeenCalledOnce();

    hub.connection.log('scan-7', '12:00:01', 'Info', 'Scanning 12 files');
    hub.connection.log('scan-7', '12:00:02', 'Warn', 'Could not decode a.heic');
    expect(lines).toEqual(['12:00:01 Info Scanning 12 files', '12:00:02 Warn Could not decode a.heic']);

    // The hub is used instead of polling, never beside it.
    await vi.advanceTimersByTimeAsync(10_000);
    expect(fetchMock).toHaveBeenCalledOnce();

    dispose();
    expect(hub.connection.stop).toHaveBeenCalledOnce();
  });

  it('is closed quietly even when the hub will not stop cleanly', async () => {
    answering(hubOffer);
    hub.stopError = new Error('already closed');
    const api = await load();

    const dispose = api.connectLogStream(() => undefined);
    await settle();
    dispose();
    await settle();

    expect(hub.connection.stop).toHaveBeenCalledOnce();
  });

  it('is not opened once it is no longer wanted', async () => {
    const offer = deferred<Response>();
    fetchMock.mockReturnValue(offer.promise);
    const api = await load();

    const dispose = api.connectLogStream(() => undefined);
    dispose();
    offer.resolve(reply(hubOffer));
    await settle();

    expect(hub.connections).toHaveLength(0);
  });

  it('is polled for when no hub can be negotiated', async () => {
    fetchMock
      .mockImplementationOnce(async () => reply('SignalR is not configured', 500))
      .mockImplementationOnce(async () => reply({ items: [line(1), line(2)], count: 2 }))
      .mockImplementationOnce(async () => reply({ count: 0 }))                 // nothing new, and no list at all
      .mockImplementationOnce(async () => reply('Rate limit exceeded', 429))
      .mockImplementationOnce(async () => { throw new TypeError('Failed to fetch'); })
      .mockImplementationOnce(async () => reply({ items: [line(3)], count: 1 }));
    const api = await load({ base: 'http://localhost:7071/api' });
    const lines: string[] = [];

    const dispose = api.connectLogStream(l => lines.push(l));
    await settle();

    expect(fetchMock.mock.calls.map(c => c[0])).toEqual([negotiate, polled]);
    expect(lines).toEqual(['12:00:01 Info line 1', '12:00:02 Info line 2']);

    // Every second and a half, through empty answers, refusals and a lost connection alike.
    for (const calls of [3, 4, 5]) {
      await vi.advanceTimersByTimeAsync(1500);
      expect(fetchMock).toHaveBeenCalledTimes(calls);
      expect(lines).toHaveLength(2);
    }
    await vi.advanceTimersByTimeAsync(1500);
    expect(lines).toEqual(['12:00:01 Info line 1', '12:00:02 Info line 2', '12:00:03 Info line 3']);
    expect(hub.connections).toHaveLength(0);

    // Dismissed between two polls: the one already scheduled does nothing.
    dispose();
    await vi.advanceTimersByTimeAsync(10_000);
    expect(fetchMock).toHaveBeenCalledTimes(6);
  });

  it('is polled for when the hub is offered but cannot be reached', async () => {
    fetchMock
      .mockImplementationOnce(async () => reply(hubOffer))
      .mockImplementation(async () => reply({ items: [line(1)] }));
    hub.startError = new Error('WebSocket failed to connect');
    const api = await load();
    const lines: string[] = [];

    const dispose = api.connectLogStream(l => lines.push(l));
    await settle();

    expect(fetchMock.mock.calls.map(c => c[0])).toEqual([negotiate, polled]);
    expect(lines).toEqual(['12:00:01 Info line 1']);
    dispose();
  });

  it('stops being polled for when dismissed while a poll is in flight', async () => {
    const poll = deferred<Response>();
    fetchMock
      .mockImplementationOnce(async () => reply(undefined, 500))
      .mockReturnValueOnce(poll.promise);
    const api = await load();

    const dispose = api.connectLogStream(() => undefined);
    await settle();
    dispose();
    poll.resolve(reply({ items: [] }));
    await vi.advanceTimersByTimeAsync(10_000);

    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('is never polled for when dismissed before the hub was refused', async () => {
    const offer = deferred<Response>();
    fetchMock.mockReturnValue(offer.promise);
    const api = await load();

    const dispose = api.connectLogStream(() => undefined);
    dispose();
    offer.resolve(reply(undefined, 500));
    await vi.advanceTimersByTimeAsync(10_000);

    expect(fetchMock).toHaveBeenCalledOnce();
  });

  it('is asked for beside wherever the service lives', async () => {
    answering(undefined, 500);
    const api = await load({ base: 'https://photos.example/backend' });

    const dispose = api.connectLogStream(() => undefined);
    await settle();
    dispose();

    expect(fetchMock.mock.calls.map(c => c[0])).toEqual(['https://photos.example/backend/api/scan/logs/negotiate', 'https://photos.example/backend/api/scan/logs?limit=200']);
  });
});
