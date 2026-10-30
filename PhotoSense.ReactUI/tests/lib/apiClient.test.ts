import { beforeEach, describe, expect, it, vi } from 'vitest';
import { deferred } from '../deferred';
import { reply } from '../fixtures';

const swr = vi.hoisted(() => ({ useSWR: vi.fn(), mutate: vi.fn() }));
vi.mock('swr', () => ({ default: swr.useSWR, mutate: swr.mutate }));

const API = 'http://localhost:7071/api';
const fetchMock = vi.fn<typeof fetch>();

beforeEach(() => {
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
    expect(isGroupListing(`${API}/scan/status`)).toBe(true);        // the count of files on record changes with them
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

  it('remove the similar shots of one group when asked for those, and never those of every group', async () => {
    answering({ removed: 2, bytes: 600, skipped: 0, companions: 0, problems: [] });
    const api = await load();

    await api.removeDuplicates('gS', 'similar');
    expect(lastRequest().url).toBe(`${API}/photos/bulk/remove-duplicates?group=gS&mode=similar`);
    await api.removeDuplicates('gS', 'duplicates');
    expect(lastRequest().url).toBe(`${API}/photos/bulk/remove-duplicates?group=gS`);
    // With no group named it still says similar, which the service refuses; it is never sent as "every duplicate".
    await api.removeDuplicates(undefined, 'similar');
    expect(lastRequest().url).toBe(`${API}/photos/bulk/remove-duplicates?mode=similar`);
  });

  it('remove the copies of several groups as one change, however many are named, and list the groups afresh', async () => {
    const done = { removed: 85, bytes: 300, skipped: 1, companions: 2, problems: ['one was left alone'], batchId: 'b1' };
    answering(done);
    const api = await load();
    const keys = Array.from({ length: 85 }, (_, i) => `g ${i}`);

    await expect(api.removeGroups(keys, 'duplicates')).resolves.toEqual(done);
    expect(fetchMock).toHaveBeenCalledOnce();
    expect(lastRequest()).toEqual({
      url: `${API}/photos/bulk/remove-duplicates`,
      init: { method: 'POST', body: JSON.stringify({ groups: keys }), headers: { 'Content-Type': 'application/json', 'x-photosense-client': 'web' } },
    });
    expect(swr.mutate).toHaveBeenCalledOnce();

    await api.removeGroups(['gS', 'gT'], 'similar');
    expect(lastRequest().url).toBe(`${API}/photos/bulk/remove-duplicates?mode=similar`);
    // With no group named nothing is asked for, least of all "every duplicate".
    fetchMock.mockClear();
    await expect(api.removeGroups([], 'duplicates')).resolves.toEqual({ removed: 0, bytes: 0, skipped: 0, companions: 0, problems: [] });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('say why the copies of several groups were not removed', async () => {
    answering('The disk is full', 500);
    const api = await load();
    await expect(api.removeGroups(['g1'], 'duplicates')).rejects.toMatchObject({ message: 'The disk is full' });
  });

  it('list the folders of the service\'s machine, from the top or inside one of them', async () => {
    const listing = { path: 'C:\\Users\\jamie\\Pictures', parent: 'C:\\Users\\jamie', folders: [{ name: "Jamie's Phone", path: "C:\\Users\\jamie\\Pictures\\Jamie's Phone" }] };
    answering(listing);
    const api = await load();

    await expect(api.browseFolders("C:\\Users\\jamie\\Pictures\\Jamie's Phone")).resolves.toEqual(listing);
    expect(lastRequest()).toEqual({
      url: `${API}/folders?path=C%3A%5CUsers%5Cjamie%5CPictures%5CJamie's%20Phone`,
      init: { headers: { 'Content-Type': 'application/json', 'x-photosense-client': 'web' } },
    });
    await api.browseFolders();
    expect(lastRequest().url).toBe(`${API}/folders`);
    await api.browseFolders('');
    expect(lastRequest().url).toBe(`${API}/folders`);
  });

  it('clear the scan results, say how many files were forgotten, and refresh the groups', async () => {
    answering({ forgotten: 6941 });
    const api = await load();
    await expect(api.clearResults()).resolves.toEqual({ forgotten: 6941 });
    expect(lastRequest()).toEqual({ url: `${API}/scan/reset`, init: { method: 'POST', headers: { 'x-photosense-client': 'web' } } });
    expect(swr.mutate).toHaveBeenCalledOnce();
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
    await expect(api.browseFolders('E:\\')).rejects.toMatchObject({ message });
    await expect(api.clearResults()).rejects.toMatchObject({ message });
    await expect(api.startScan({ primaryLocation: 'C:\\nowhere', recursive: true })).rejects.toMatchObject({ message });
    // Nothing changed, so nothing is refreshed.
    expect(swr.mutate).not.toHaveBeenCalled();
  });
});

describe('what a phone lets PhotoSense see', () => {
  it('is asked of the program, and the phone\'s picker answers with what it then allows, which the listings follow', async () => {
    swr.useSWR.mockReturnValue({ data: 'the access' });
    answering({ access: 'everything' });
    const api = await load();

    expect(api.usePhotoAccess()).toEqual({ data: 'the access' });
    expect(swr.useSWR).toHaveBeenLastCalledWith(`${API}/library/access`, expect.any(Function));

    await expect(api.chooseMorePhotos()).resolves.toEqual({ access: 'everything' });
    expect(lastRequest().url).toBe(`${API}/library/choose-more`);
    expect(lastRequest().init?.method).toBe('POST');
    expect(swr.mutate).toHaveBeenCalledWith(`${API}/library/access`, { access: 'everything' }, { revalidate: false });
    const listings = swr.mutate.mock.calls[1][0] as (key: unknown) => boolean;
    expect([`${API}/organize/files?root=x`, `${API}/scan/groups`, 7].map(listings)).toEqual([true, false, false]);
  });

  it('opens PhotoSense\'s page of the phone\'s settings', async () => {
    answering(undefined, 204);
    const api = await load();
    await api.openPhotoSettings();
    expect(lastRequest().url).toBe(`${API}/library/open-settings`);
    expect(lastRequest().init?.method).toBe('POST');
  });
});

describe('About', () => {  it('shows the icon the program answering hands out, and asks it who makes PhotoSense and which version is running', async () => {
    swr.useSWR.mockReturnValue({ data: 'the details' });
    const api = await load();

    expect(api.appIconUrl).toBe(`${API}/about/icon`);
    expect(api.useAbout()).toEqual({ data: 'the details' });
    expect(swr.useSWR).toHaveBeenLastCalledWith(`${API}/about`, expect.any(Function));
  });

  it('asks for each document as the text it is, and says why one could not be had', async () => {
    const api = await load();
    api.useAboutText('privacy');
    const [url, fetcher] = swr.useSWR.mock.calls[0] as [string, (url: string) => Promise<string>];
    expect(url).toBe(`${API}/about/privacy`);

    answering('# Privacy Policy');
    await expect(fetcher(url)).resolves.toBe('# Privacy Policy');
    expect(lastRequest()).toEqual({ url, init: undefined });

    answering('There is no document called cookies.', 404);
    await expect(fetcher(`${API}/about/cookies`)).rejects.toThrow('There is no document called cookies.');
  });
});

describe('what the page reads', () => {  it('asks for groups fifty to a page, by mode, page, search text and whether reviewed ones are hidden', async () => {
    swr.useSWR.mockReturnValue({ data: 'the groups' });
    const api = await load();

    expect(api.useGroups('duplicates', '', 1, false)).toEqual({ data: 'the groups' });
    expect(swr.useSWR).toHaveBeenLastCalledWith(`${API}/scan/groups?mode=duplicates&page=1&pageSize=50&hideKept=false&q=`, expect.any(Function), { refreshInterval: 5000, keepPreviousData: true });

    api.useGroups('similar', 'trip & beach', 3, true);
    expect(swr.useSWR.mock.calls[1][0]).toBe(`${API}/scan/groups?mode=similar&page=3&pageSize=50&hideKept=true&q=trip%20%26%20beach`);
  });

  it('fetches a page ahead of its being turned to, with the pictures its tiles show', async () => {
    const pictures: string[] = [];
    vi.stubGlobal('Image', class { set src(url: string) { pictures.push(url); } });
    swr.mutate.mockImplementation(async (_key: string, data: Promise<unknown>) => data);
    answering({ mode: 'duplicates', items: [{ keeper: { id: 'p1', isVideo: false } }, { keeper: { id: 'v1', isVideo: true } }, { keeper: { id: 'p2', isVideo: false } }] });
    const api = await load();

    await api.prefetchGroups('duplicates', 'trip', 2, true);

    const url = `${API}/scan/groups?mode=duplicates&page=2&pageSize=50&hideKept=true&q=trip`;
    expect(lastRequest().url).toBe(url);
    // Left where the page's own request will find it, without asking for it a second time.
    expect(swr.mutate).toHaveBeenCalledExactlyOnceWith(url, expect.any(Promise), { revalidate: false });
    expect(pictures).toEqual([api.thumbnailUrl('p1'), api.thumbnailUrl('p2')]);
  });

  it('makes nothing of a page that could not be fetched ahead', async () => {
    const pictures: string[] = [];
    vi.stubGlobal('Image', class { set src(url: string) { pictures.push(url); } });
    swr.mutate.mockImplementation(async (_key: string, data: Promise<unknown>) => data);
    answering('The database is busy', 500);
    const api = await load();
    await expect(api.prefetchGroups('similar', '', 2, false)).resolves.toBeUndefined();
    expect(pictures).toEqual([]);
  });

  it('asks how many files are on record', async () => {
    swr.useSWR.mockReturnValue({ data: { totalPhotos: 6941 } });
    const api = await load();
    expect(api.useScanStatus()).toEqual({ data: { totalPhotos: 6941 } });
    expect(swr.useSWR).toHaveBeenLastCalledWith(`${API}/scan/status`, expect.any(Function), { refreshInterval: 5000 });
  });

  it('asks afresh how many groups of each kind there are', async () => {
    fetchMock.mockImplementation(async url => reply({ total: String(url).includes('mode=similar') ? 170 : 263 }));
    const api = await load();
    await expect(api.fetchGroupTotals()).resolves.toEqual({ duplicates: 263, similar: 170 });
    expect(fetchMock.mock.calls.map(c => c[0])).toEqual([`${API}/scan/groups?mode=duplicates&page=1&pageSize=50&hideKept=false&q=`, `${API}/scan/groups?mode=similar&page=1&pageSize=50&hideKept=false&q=`]);
  });

  it('asks again for everything on screen when told to retry', async () => {
    const api = await load();
    await api.retryNow();
    const everything = swr.mutate.mock.calls[0][0] as (key: unknown) => boolean;
    expect(everything('anything')).toBe(true);
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

describe('organize', () => {
  const destination = { basePath: 'C:\\photos', folderName: 'Trips\\Glacier 2022', direct: false };
  const json = { 'Content-Type': 'application/json', 'x-photosense-client': 'web' };

  it('shows a file by its id', async () => {
    const api = await load();
    expect(api.organizeThumbnailUrl('abc')).toBe(`${API}/organize/files/abc/thumbnail`);
    expect(api.organizeImageUrl('abc')).toBe(`${API}/organize/files/abc/image`);
  });

  it('lists a folder only once one is chosen, and does not ask again merely because the window came forward', async () => {
    swr.useSWR.mockReturnValue({ data: 'listing' });
    const api = await load();
    expect(api.useOrganizeFiles()).toEqual({ data: 'listing' });
    expect(swr.useSWR).toHaveBeenLastCalledWith(null, expect.any(Function), { revalidateOnFocus: false, shouldRetryOnError: false });
    api.useOrganizeFiles('C:\\Phone Pictures');
    expect(swr.useSWR.mock.calls[1][0]).toBe(`${API}/organize/files?root=C%3A%5CPhone%20Pictures`);
    api.useOrganizeBatches();
    expect(swr.useSWR).toHaveBeenLastCalledWith(`${API}/organize/batches`, expect.any(Function), { revalidateOnFocus: false });
  });

  it('asks how far the reading of a folder has got, saying how much of it the page already has', async () => {
    const progress = { reading: true, readingId: 'r1', total: 9000, done: 4100, from: 4000, more: false, files: [], places: [] };
    answering(progress);
    const api = await load();
    await expect(api.fetchOrganizeProgress('C:\\Phone Pictures', 4000, 12)).resolves.toEqual(progress);
    expect(lastRequest()).toEqual({ url: `${API}/organize/progress?root=C%3A%5CPhone%20Pictures&from=4000&places=12`, init: { headers: json } });
  });

  it('asks where files would go without moving them', async () => {
    answering({ destination: 'C:\\photos\\Trips', items: [] });
    const api = await load();
    await expect(api.planOrganize(destination, [{ id: 'a', subfolder: '2022' }, { id: 'b' }])).resolves.toEqual({ destination: 'C:\\photos\\Trips', items: [] });
    expect(lastRequest()).toEqual({ url: `${API}/organize/plan`, init: { method: 'POST', body: JSON.stringify({ ...destination, files: [{ id: 'a', subfolder: '2022' }, { id: 'b' }] }), headers: json } });
    expect(swr.mutate).not.toHaveBeenCalled();
  });

  it('moves or copies files, undoes that, and each time asks again for what both areas show', async () => {
    const api = await load();
    const request = { ...destination, mode: 'move' as const, companions: true, label: 'Trips\\Glacier 2022', files: [{ id: 'a', name: 'IMG_1.JPG' }] };
    answering({ batchId: 'b1', done: 1 });
    await expect(api.applyOrganize(request)).resolves.toEqual({ batchId: 'b1', done: 1 });
    expect(lastRequest()).toEqual({ url: `${API}/organize/apply`, init: { method: 'POST', body: JSON.stringify(request), headers: json } });

    const [organize, groups] = swr.mutate.mock.calls.map(c => c[0] as (key: unknown) => boolean);
    // The listing and what was moved are asked for again; how far a reading has got is not something a move changes.
    expect([`${API}/organize/files?root=x`, `${API}/organize/batches`, `${API}/organize/progress?root=x`, `${API}/scan/groups?mode=duplicates`, 7].map(organize)).toEqual([true, true, false, false, false]);
    expect(groups(`${API}/scan/groups?mode=duplicates`)).toBe(true);

    // Deleting files changes the same lists.
    swr.mutate.mockClear();
    answering({ batchId: 'b2', done: 2 });
    await expect(api.removeOrganize('C:\\photos', ['a', 'b'])).resolves.toEqual({ batchId: 'b2', done: 2 });
    expect(lastRequest()).toEqual({ url: `${API}/organize/remove`, init: { method: 'POST', body: JSON.stringify({ root: 'C:\\photos', files: [{ id: 'a' }, { id: 'b' }] }), headers: json } });
    expect(swr.mutate).toHaveBeenCalledTimes(2);

    swr.mutate.mockClear();
    answering({ restored: 1, skipped: 0, problems: [] });
    await expect(api.undoChange('b 1')).resolves.toEqual({ restored: 1, skipped: 0, problems: [] });
    expect(lastRequest()).toEqual({ url: `${API}/organize/undo/b%201`, init: { method: 'POST', headers: { 'x-photosense-client': 'web' } } });
    expect(swr.mutate).toHaveBeenCalledTimes(2);

    answering('There is nothing to undo: this was undone already, or was never done.', 404);
    await expect(api.undoChange('b1')).rejects.toMatchObject({ message: 'There is nothing to undo: this was undone already, or was never done.' });
  });

  it('opens a file in the default viewer of the service\'s machine', async () => {
    answering(undefined, 204);
    const api = await load();
    await api.openOrganizeFile('abc');
    expect(lastRequest()).toEqual({ url: `${API}/organize/files/abc/open`, init: { method: 'POST', headers: { 'x-photosense-client': 'web' } } });
  });
});

describe('removed files', () => {
  const json = { 'Content-Type': 'application/json', 'x-photosense-client': 'web' };

  it('are counted in and around the folders the page knows of', async () => {
    const found = { folders: [{ path: 'C:\\photos\\_PhotoSense_Removed', files: 3, bytes: 900 }], files: 3, bytes: 900 };
    answering(found);
    const api = await load();
    await expect(api.fetchRemoved(['C:\\photos', 'D:\\Trips & days'])).resolves.toEqual(found);
    expect(lastRequest()).toEqual({ url: `${API}/removed?root=C%3A%5Cphotos&root=D%3A%5CTrips%20%26%20days`, init: { headers: json } });
    // With no folder known here, the service goes by what it has on record.
    await api.fetchRemoved([]);
    expect(lastRequest().url).toBe(`${API}/removed?`);
  });

  it('are erased from the folders named, and what could have been undone is listed afresh', async () => {
    const erased = { erased: 3, bytes: 900, skipped: 0, problems: [] };
    answering(erased);
    const api = await load();
    await expect(api.eraseRemoved(['C:\\photos\\_PhotoSense_Removed'])).resolves.toEqual(erased);
    expect(lastRequest()).toEqual({ url: `${API}/removed/erase`, init: { method: 'POST', body: JSON.stringify({ folders: ['C:\\photos\\_PhotoSense_Removed'] }), headers: json } });
    const matches = swr.mutate.mock.calls[0][0] as (key: unknown) => boolean;
    expect([`${API}/organize/batches`, `${API}/organize/files?root=x`, 7].map(matches)).toEqual([true, false, false]);

    swr.mutate.mockClear();
    answering('Not a folder of removed files: C:\\photos', 400);
    await expect(api.eraseRemoved(['C:\\photos'])).rejects.toMatchObject({ message: 'Not a folder of removed files: C:\\photos' });
    expect(swr.mutate).not.toHaveBeenCalled();
  });
});

describe('the log stream', () => {
  const polled = `${API}/scan/logs?limit=200`;
  const settle = () => vi.advanceTimersByTimeAsync(0);
  const line = (n: number) => ({ timestamp: `12:00:0${n}`, level: 'Info', message: `line ${n}` });

  beforeEach(() => { vi.useFakeTimers(); });

  it('is asked for every second and a half, through empty answers, refusals and a lost connection alike', async () => {
    fetchMock
      .mockImplementationOnce(async () => reply({ items: [line(1), line(2)], count: 2 }))
      .mockImplementationOnce(async () => reply({ count: 0 }))                 // nothing new, and no list at all
      .mockImplementationOnce(async () => reply('Rate limit exceeded', 429))
      .mockImplementationOnce(async () => { throw new TypeError('Failed to fetch'); })
      .mockImplementationOnce(async () => reply({ items: [line(3)], count: 1 }));
    const api = await load({ base: 'http://localhost:7071/api' });
    const lines: string[] = [];

    const dispose = api.connectLogStream(l => lines.push(l));
    await settle();

    // The service shows its log only to the UI, so the request says where it comes from.
    expect(fetchMock.mock.calls).toEqual([[polled, { headers: { 'x-photosense-client': 'web' } }]]);
    expect(lines).toEqual(['12:00:01 Info line 1', '12:00:02 Info line 2']);

    for (const calls of [2, 3, 4]) {
      await vi.advanceTimersByTimeAsync(1500);
      expect(fetchMock).toHaveBeenCalledTimes(calls);
      expect(lines).toHaveLength(2);
    }
    await vi.advanceTimersByTimeAsync(1500);
    expect(lines).toEqual(['12:00:01 Info line 1', '12:00:02 Info line 2', '12:00:03 Info line 3']);

    // Dismissed between two polls: the one already scheduled does nothing.
    dispose();
    await vi.advanceTimersByTimeAsync(10_000);
    expect(fetchMock).toHaveBeenCalledTimes(5);
  });

  it('stops being asked for when dismissed while a poll is in flight', async () => {
    const poll = deferred<Response>();
    fetchMock.mockReturnValueOnce(poll.promise);
    const api = await load();

    const dispose = api.connectLogStream(() => undefined);
    dispose();
    poll.resolve(reply({ items: [] }));
    await vi.advanceTimersByTimeAsync(10_000);

    expect(fetchMock).toHaveBeenCalledOnce();
  });

  it('is asked for where the rest of the service is', async () => {
    answering(undefined, 500);
    const api = await load({ base: 'https://photos.example/backend' });

    const dispose = api.connectLogStream(() => undefined);
    await settle();
    dispose();

    expect(fetchMock.mock.calls.map(c => c[0])).toEqual(['https://photos.example/backend/scan/logs?limit=200']);
  });
});

describe('sending photos from a phone to a computer', () => {
  const json = { 'Content-Type': 'application/json', 'x-photosense-client': 'web' };
  const state = { stage: 'waiting', code: null, computer: null, done: 0, total: 2, bytes: 0, totalBytes: 900, deletable: [], where: null, phone: 'iPhone', problem: null };

  it('follows the phone every second, and shows each answer of the phone at once', async () => {
    swr.useSWR.mockReturnValue({ data: state });
    const api = await load();
    expect(api.useSendState()).toEqual({ data: state });
    expect(swr.useSWR).toHaveBeenLastCalledWith(`${API}/send/state`, expect.any(Function), { refreshInterval: 1000 });

    answering(state);
    await expect(api.startSending(['f1', 'f2'])).resolves.toEqual(state);
    expect(lastRequest()).toEqual({ url: `${API}/send/start`, init: { method: 'POST', body: JSON.stringify({ files: ['f1', 'f2'] }), headers: json } });
    expect(swr.mutate).toHaveBeenLastCalledWith(`${API}/send/state`, state, { revalidate: false });
    await api.confirmSending(true);
    expect(lastRequest()).toEqual({ url: `${API}/send/confirm`, init: { method: 'POST', body: JSON.stringify({ same: true }), headers: json } });
    await api.stopSending();
    expect(lastRequest()).toEqual({ url: `${API}/send/stop`, init: { method: 'POST', headers: json } });
  });

  it('deletes from the phone what is safely on the computer, and then shows the phone and Organize afresh', async () => {
    answering({ deleted: 2 });
    const api = await load();
    await expect(api.deleteSent()).resolves.toEqual({ deleted: 2 });
    expect(lastRequest()).toEqual({ url: `${API}/send/delete-sent`, init: { method: 'POST', headers: json } });
    expect(swr.mutate).toHaveBeenCalledWith(`${API}/send/state`);
    const matches = swr.mutate.mock.calls[0][0] as (key: unknown) => boolean;
    expect([`${API}/organize/files?root=x`, `${API}/send/state`].map(matches)).toEqual([true, false]);
  });

  it('looks for phones and follows receiving only while each is on screen', async () => {
    const api = await load();
    api.usePhones(true);
    expect(swr.useSWR).toHaveBeenLastCalledWith(`${API}/phones`, expect.any(Function), { refreshInterval: 3000 });
    api.usePhones(false);
    expect(swr.useSWR.mock.lastCall![0]).toBeNull();
    api.useReceiveState(true);
    expect(swr.useSWR).toHaveBeenLastCalledWith(`${API}/receive/state`, expect.any(Function), { refreshInterval: 1000 });
    api.useReceiveState(false);
    expect(swr.useSWR.mock.lastCall![0]).toBeNull();

    const receiving = { stage: 'connecting', code: null, phone: 'iPhone', done: 0, total: 0, bytes: 0, totalBytes: 0, alreadyThere: 0, folder: 'C:\Photos', problem: null };
    answering(receiving);
    await expect(api.startReceiving('wifi:1', 'C:\Photos')).resolves.toEqual(receiving);
    expect(lastRequest()).toEqual({ url: `${API}/receive/start`, init: { method: 'POST', body: JSON.stringify({ phone: 'wifi:1', folder: 'C:\Photos' }), headers: json } });
    expect(swr.mutate).toHaveBeenLastCalledWith(`${API}/receive/state`, receiving, { revalidate: false });
    await api.confirmReceiving(false);
    expect(lastRequest()).toEqual({ url: `${API}/receive/confirm`, init: { method: 'POST', body: JSON.stringify({ same: false }), headers: json } });
    await api.stopReceiving();
    expect(lastRequest()).toEqual({ url: `${API}/receive/stop`, init: { method: 'POST', headers: json } });
  });
});

describe('on Android', () => {
  it('hands each request body over beside its request, which carries the number the app gave it', async () => {
    const keep = vi.fn(() => 'body-7');
    vi.stubGlobal('PhotoSenseAndroid', { keep });
    try {
      answering({ instanceId: 'scan-1' });
      const api = await load();
      await api.startScan({ primaryLocation: '/Phone', secondaryLocation: '', recursive: true });
      expect(keep).toHaveBeenCalledExactlyOnceWith(JSON.stringify({ primaryLocation: '/Phone', secondaryLocation: '', recursive: true }));
      expect(lastRequest().init?.headers).toMatchObject({ 'x-photosense-body': 'body-7' });
      // A request with no body carries nothing.
      await api.stopSending();
      expect(keep).toHaveBeenCalledOnce();
      expect(lastRequest().init?.headers).not.toHaveProperty('x-photosense-body');
    } finally {
      vi.unstubAllGlobals();
    }
  });
});
