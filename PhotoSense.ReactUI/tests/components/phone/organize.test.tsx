import { act, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { OrganizeScreen } from '../../../components/phone/Organize';
import type { Album, Library } from '../../../lib/library';
import { joinPath } from '../../../lib/organize';
import type { Platform } from '../../../lib/phone';
import { useOrganize } from '../../../lib/phoneOrganize';
import type { OrganizeApplyDto, OrganizeBatchDto, OrganizeClashDto, OrganizeDestination, OrganizeFileDto, OrganizePlanFile } from '../../../types';
import { deferred } from '../../deferred';
import { organizeFile, organizePlace } from '../../fixtures';
import { frame, inFrame } from './frame';

const api = vi.hoisted(() => ({ planOrganize: vi.fn(), applyOrganize: vi.fn(), removeOrganize: vi.fn(), undoChange: vi.fn(), useOrganizeBatches: vi.fn() }));
vi.mock('../../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/apiClient')>()), ...api }));

const ROOT = '/Photo Library';
const places = [organizePlace(), organizePlace({ town: 'Butte', area: 'Uptown Butte', latitude: 46.0038, longitude: -112.5348 })];
const at = (id: string, more: Partial<OrganizeFileDto> = {}) => organizeFile({ id, name: `${id}.JPG`, folder: ROOT, sizeBytes: 1024 ** 2, ...more });

// What the phone holds, which the mocked calls change as the service would. Like SWR, the library is drawn again
// whenever it changes.
let files: OrganizeFileDto[];
let reading: Library['reading'];
let clashes: OrganizeClashDto[];
let taken: Record<string, string[]>;
let batches: OrganizeBatchDto[];
let version = 0;
const watching = new Set<() => void>();
function changed() { version++; watching.forEach(f => f()); }
const useVersion = () => React.useSyncExternalStore(f => { watching.add(f); return () => watching.delete(f); }, () => version);
const applied = (more: Partial<OrganizeApplyDto> = {}): OrganizeApplyDto => ({ batchId: 'b1', done: 4, bytes: 4 * 1024 ** 2, companions: 0, renamed: 0, skipped: 0, problems: [], items: [], ...more });

beforeEach(() => {
  for (const mock of Object.values(api)) mock.mockReset();
  files = [
    at('a1', { place: 0, date: '2022-05-01T10:00:00' }), at('a2', { place: 0, date: '2023-06-01T10:00:00' }),
    at('a3', { place: 0, date: '2023-07-01T10:00:00', isVideo: true, durationSeconds: 12 }), at('a4', { place: 0, date: '2023-08-01T10:00:00' }),
    at('b1', { place: 1, date: '2021-01-01T10:00:00' }), at('n1', { date: '2020-01-01T10:00:00' }), at('s1', { place: 0, folder: `${ROOT}/Trips/Montana` }),
  ];
  reading = undefined;
  clashes = [];
  taken = {};
  batches = [];
  api.useOrganizeBatches.mockImplementation(() => ({ data: batches }));
  api.planOrganize.mockImplementation(async (d: OrganizeDestination, asked: OrganizePlanFile[]) => ({
    destination: joinPath(d.basePath, d.folderName), exists: false, items: asked.map(a => ({ id: a.id, folder: joinPath(d.basePath, d.folderName, a.subfolder) })), clashes, taken, companions: 0,
  }));
  // Moving puts the files where they were asked to go.
  api.applyOrganize.mockImplementation(async (r: { basePath: string; folderName: string; files: { id: string; subfolder?: string }[] }) => {
    files = files.map(f => { const going = r.files.find(x => x.id === f.id); return going ? { ...f, folder: joinPath(r.basePath, r.folderName, going.subfolder) } : f; });
    changed();
    return applied({ done: r.files.length });
  });
  api.removeOrganize.mockImplementation(async (_root: string, ids: string[]) => {
    files = files.filter(f => !ids.includes(f.id));
    clashes = [];
    taken = {};
    changed();
    return applied({ done: ids.length, batchId: 'r1' });
  });
  api.undoChange.mockResolvedValue({ restored: 4, skipped: 0, problems: [] });
});

interface ShowProps { platform?: Platform; tablet?: boolean; made?: Album[] }

/** Organize in a phone's or tablet's frame, holding its state as PhoneApp does. */
function show({ platform = 'ios', tablet = false, made = [] }: ShowProps = {}) {
  const notify = vi.fn(), onMade = vi.fn(), onNoLocation = vi.fn();
  const root = platform === 'ios' ? ROOT : '/Phone';
  function Holder() {
    useVersion();
    const listing = { root, fromScan: true, places, files };
    const library: Library = { root, files, listing, reading };
    const organize = useOrganize(listing, platform, tablet);
    return <OrganizeScreen organize={organize} library={library} made={made} onMade={onMade} onNoLocation={onNoLocation} notify={notify} />;
  }
  const view = inFrame(<Holder />, frame(platform, { tablet, form: tablet ? 'tablet' : 'phone' }));
  return { ...view, notify, onMade, onNoLocation };
}

const rows = () => within(screen.getByRole('list', { name: 'Suggestions' })).getAllByRole('button');
const row = (name: string) => within(screen.getByRole('list', { name: 'Suggestions' })).getByRole('button', { name: new RegExp(`^${name}`) });
const sheet = (name: string | RegExp) => screen.getByRole('dialog', { name });
const toastAction = (notify: ReturnType<typeof vi.fn>) => (notify.mock.calls.at(-1)![2] as { run(): void }).run;
const tile = (name: string) => screen.getByRole('button', { name });

describe('Organize\'s list', () => {
  it('suggests albums for what is in none yet, says how it sorts, and leads to what has no location and what was moved', async () => {
    const { onNoLocation } = show();
    expect(screen.getByRole('heading', { level: 1, name: 'Organize' })).toBeInTheDocument();
    expect(screen.getByText('Sort Photo Library into albums by place and date')).toBeInTheDocument();
    expect(screen.getByText('Within 5 mi · Landmark or area · Place › Year')).toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'Suggested albums' })).getByText('2')).toBeInTheDocument();
    expect(rows().map(r => r.textContent)).toEqual([
      'Anaconda4 files · 4.0 MBMay 2022 – Aug 2023No landmark known here, so it is named after the town.',
      'Uptown Butte1 file · 1.0 MBJan 2021',
    ]);
    // Four pictures of the album, a video by a play mark.
    expect(row('Anaconda').querySelectorAll('img')).toHaveLength(3);
    await userEvent.click(screen.getByRole('button', { name: '1 file has no location. It stays where it is.' }));
    expect(onNoLocation).toHaveBeenCalledOnce();
    expect(screen.getByRole('button', { name: 'Recently moved 0' })).toBeInTheDocument();
  });

  it('by date lists an album for each year, the latest first, with the places inside, in the platform\'s words', async () => {
    files = files.map(f => ({ ...f, folder: f.folder.replace(ROOT, '/Phone') }));
    show({ platform: 'android' });
    expect(screen.getByText('Sort Phone into folders by place and date')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('radio', { name: 'Date' }));
    expect(screen.getByText('By year · Year/Place')).toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'Folders by year' })).getByText('4')).toBeInTheDocument();
    expect(rows().map(r => r.textContent)).toEqual([
      '20233 files · 3.0 MBJun 2023 – Aug 20231 place inside', '20221 file · 1.0 MBMay 20221 place inside',
      '20211 file · 1.0 MBJan 20211 place inside', '20201 file · 1.0 MBJan 2020No location on most',
    ]);
    // No map by date, and nothing said of files with no location, which go into their year.
    expect(screen.queryByRole('button', { name: 'Show the map' })).not.toBeInTheDocument();
    expect(screen.queryByText(/have no location|has no location/)).not.toBeInTheDocument();
  });

  it('says when there is nothing left to sort, and nothing while the library is still being read but how far it has got', async () => {
    files = files.filter(f => f.place == null);
    const view = show();
    expect(screen.getByText('Every photo with a location is in an album.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('radio', { name: 'Date' }));
    files = [];
    changed();
    expect(await screen.findByText('Every photo is in an album.')).toBeInTheDocument();
    reading = { done: 3, total: 9 };
    changed();
    expect(await screen.findByRole('status')).toHaveTextContent('Reading your photos… 3 of 9');
    expect(screen.queryByText(/Every photo/)).not.toBeInTheDocument();
    reading = { done: 0, total: 0 };
    changed();
    expect(await screen.findByRole('status')).toHaveTextContent('Reading your photos…');
    view.unmount();
  });

  it('while reading, a suggestion is not previewed until every file is in', async () => {
    reading = { done: 3, total: 9 };
    const { notify } = show();
    await userEvent.click(row('Anaconda'));
    expect(notify).toHaveBeenCalledWith('Still reading your photos. Preview an album once every file is in.', 'info');
    expect(screen.queryByRole('region', { name: 'Preview' })).not.toBeInTheDocument();
  });

  it('shows the suggestions as dots on a plain map, the one picked as a card with Preview', async () => {
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Show the map' }));
    const map = screen.getByRole('group', { name: 'Map' });
    expect(map).toHaveTextContent('Drawn on this phone. Positions are never sent anywhere.');
    const dots = within(map).getAllByRole('button');
    expect(dots.map(d => d.getAttribute('aria-label'))).toEqual(['Anaconda · 4 files', 'Uptown Butte · 1 file']);
    expect(dots[0]).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(dots[1]);
    expect(dots[1]).toHaveAttribute('aria-pressed', 'true');
    expect(within(dots[1]).getByText('Uptown Butte')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Preview' }));
    expect(screen.getByRole('heading', { name: 'Uptown Butte' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Back' }));
    await userEvent.click(screen.getByRole('button', { name: 'Show the list' }));
    expect(screen.getByRole('list', { name: 'Suggestions' })).toBeInTheDocument();
  });

  it('has no map when nothing has a place', async () => {
    files = [at('n1'), at('n2')];
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Show the map' }));
    expect(screen.queryByRole('group', { name: 'Map' })).not.toBeInTheDocument();
  });
});

describe('Organize\'s options', () => {
  it('groups by place or date, combines near places within a distance, names albums, and sorts one kind of file', async () => {
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Options' }));
    const options = sheet('Album options');
    expect(options).toHaveTextContent('Albums are made in Photo Library. Files already in one of your albums are left out.');
    expect(options).toHaveTextContent('A landmark or area is used where a photo was taken at one. Otherwise the town is used.');
    await userEvent.click(within(options).getByRole('radio', { name: 'Keep separate' }));
    expect(within(options).queryByRole('group', { name: 'Within' })).not.toBeInTheDocument();
    await userEvent.click(within(options).getByRole('radio', { name: 'Combine' }));
    await userEvent.click(within(options).getByRole('button', { name: '15 mi' }));
    expect(within(options).getByRole('button', { name: '15 mi' })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(within(options).getByRole('radio', { name: 'Town' }));
    expect(options).not.toHaveTextContent('A landmark or area is used');
    await userEvent.click(within(options).getByRole('radio', { name: 'Month' }));
    await userEvent.click(within(options).getByRole('radio', { name: 'Photos' }));
    expect(options).toHaveTextContent('Only the kind shown is sorted. A Live Photo\'s video always goes with its picture.');
    expect(screen.getByText('Within 15 mi · Town · Place › Month · Photos only')).toBeInTheDocument();
    await userEvent.click(within(options).getByRole('radio', { name: 'Date' }));
    await userEvent.click(within(options).getByRole('radio', { name: 'Nothing' }));
    expect(screen.getByText('By year · Year · Photos only')).toBeInTheDocument();
    await userEvent.click(within(options).getByRole('button', { name: 'Done' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('on Android says folders are made in Pictures', async () => {
    show({ platform: 'android' });
    await userEvent.click(screen.getByRole('button', { name: 'Options' }));
    expect(sheet('Folder options')).toHaveTextContent('Folders are made in Pictures. Files already in one of your folders are left out.');
  });
});

describe('a suggestion\'s preview', () => {
  it('on a phone is pushed over the list: its name, where it is made, what is inside, and its files by year, any of which can stay', async () => {
    show();
    await userEvent.click(row('Anaconda'));
    expect(screen.getByRole('region', { name: 'Preview' })).toBeInTheDocument();
    expect(screen.getByText('Preview. Nothing moves until you tap Move.')).toBeInTheDocument();
    expect(await screen.findByText('4 of 4 files will move. Tap a file to leave it where it is.')).toBeInTheDocument();
    expect(screen.getByRole('textbox')).toHaveValue('Anaconda');
    expect(screen.getByText('Place › Year')).toBeInTheDocument();
    expect(screen.getAllByRole('region').map(r => r.getAttribute('aria-label'))).toEqual(['Preview', '2022', '2023']);
    expect(within(screen.getByRole('region', { name: '2023' })).getByText('3 files')).toBeInTheDocument();
    expect(tile('a3.JPG')).toHaveTextContent('0:12');
    expect(screen.getByText('Into Photo Library › Anaconda. Undo puts them back.')).toBeInTheDocument();
    await userEvent.click(tile('a2.JPG'));
    expect(tile('a2.JPG')).toHaveAttribute('aria-pressed', 'false');
    expect(tile('a2.JPG')).toHaveTextContent('Stays');
    expect(screen.getByText('3 of 4 files will move. Tap a file to leave it where it is.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Move 3 files' })).toBeEnabled();
    await userEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(screen.getByRole('list', { name: 'Suggestions' })).toBeInTheDocument();
  });

  it('renames the album, saying what is wrong with a name that cannot be one', async () => {
    show();
    await userEvent.click(row('Anaconda'));
    const name = screen.getByRole('textbox');
    await userEvent.clear(name);
    expect(screen.getByRole('alert')).toHaveTextContent('Give the new album a name.');
    expect(screen.getByRole('heading', { name: 'New album' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Move/ })).toBeDisabled();
    await userEvent.type(name, 'Home?');
    expect(screen.getByRole('alert')).toHaveTextContent('Album names cannot contain');
    await userEvent.type(name, '{Backspace}');
    expect(await screen.findByText('Into Photo Library › Home. Undo puts them back.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Home' })).toBeInTheDocument();
  });

  it('says what is wrong on Android in its words, and when the plan cannot be made', async () => {
    api.planOrganize.mockRejectedValueOnce(new Error('The library is locked.')).mockRejectedValueOnce('no answer');
    files = files.map(f => ({ ...f, folder: f.folder.replace(ROOT, '/Phone') }));
    show({ platform: 'android' });
    await userEvent.click(row('Anaconda'));
    expect(await screen.findByRole('alert')).toHaveTextContent('The library is locked.');
    await userEvent.clear(screen.getByRole('textbox'));
    expect(screen.getByRole('alert')).toHaveTextContent('Give the new folder a name.');
    await userEvent.type(screen.getByRole('textbox'), 'B');
    expect(await screen.findByText('no answer')).toBeInTheDocument();
  });

  it('shows two rows of a large year, more on Show more, and fewer again', async () => {
    files = Array.from({ length: 30 }, (_, i) => at(`f${i}`, { place: 0, date: '2023-01-01T00:00:00' }));
    show();
    await userEvent.click(row('Anaconda'));
    const year = () => screen.getByRole('region', { name: '2023' });
    expect(within(year()).getAllByRole('button', { name: /^f/ })).toHaveLength(8);
    await userEvent.click(within(year()).getByRole('button', { name: 'Show more' }));
    expect(within(year()).getAllByRole('button', { name: /^f/ })).toHaveLength(20);
    await userEvent.click(within(year()).getByRole('button', { name: 'Show more' }));
    expect(within(year()).getAllByRole('button', { name: /^f/ })).toHaveLength(30);
    await userEvent.click(within(year()).getByRole('button', { name: 'Show fewer' }));
    expect(within(year()).getAllByRole('button', { name: /^f/ })).toHaveLength(8);
  });

  it('with nothing inside shows all its files in one section', async () => {
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Options' }));
    await userEvent.click(within(sheet('Album options')).getByRole('radio', { name: 'Nothing' }));
    await userEvent.click(within(sheet('Album options')).getByRole('button', { name: 'Done' }));
    await userEvent.click(row('Anaconda'));
    expect(screen.getByRole('region', { name: 'All files' })).toBeInTheDocument();
  });

  it('is made in the place chosen: the top of the library or a folder of albums', async () => {
    show();
    await userEvent.click(row('Anaconda'));
    await userEvent.click(screen.getByRole('button', { name: 'Change' }));
    const where = sheet('Where the album goes');
    expect(where).toHaveTextContent('A folder here is a folder of albums in the Photos app.');
    expect(within(where).getByRole('radio', { name: /^Photo Library/ })).toHaveAttribute('aria-checked', 'true');
    await userEvent.click(within(where).getByRole('radio', { name: /^Trips/ }));
    await userEvent.click(within(where).getByRole('button', { name: 'Done' }));
    expect(await screen.findByText('Into Photo Library › Trips › Anaconda. Undo puts them back.')).toBeInTheDocument();
    expect(api.planOrganize).toHaveBeenLastCalledWith({ basePath: `${ROOT}/Trips`, folderName: 'Anaconda', direct: false }, expect.any(Array));
  });

  it('on Android is made in Pictures unless another place is chosen', async () => {
    files = files.map(f => ({ ...f, folder: f.folder.replace(ROOT, '/Phone') }));
    show({ platform: 'android' });
    await userEvent.click(row('Anaconda'));
    expect(await screen.findByText('Into Pictures/Anaconda. Undo puts them back.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Change' }));
    expect(sheet('Where the folder goes')).toHaveTextContent('Android lets apps make folders only inside DCIM, Pictures or Movies.');
  });
});

describe('moving', () => {
  it('moves the files that go, says so with Undo, and shows the suggestion as moved; Undo puts them back', async () => {
    const { notify } = show();
    await userEvent.click(row('Anaconda'));
    await userEvent.click(await screen.findByRole('button', { name: 'Move 4 files' }));
    expect(api.applyOrganize).toHaveBeenCalledWith({
      basePath: ROOT, folderName: 'Anaconda', direct: false, mode: 'move', companions: true, label: 'Anaconda',
      files: [{ id: 'a1', name: 'a1.JPG', subfolder: '2022' }, { id: 'a2', name: 'a2.JPG', subfolder: '2023' }, { id: 'a3', name: 'a3.JPG', subfolder: '2023' }, { id: 'a4', name: 'a4.JPG', subfolder: '2023' }],
    });
    expect(notify).toHaveBeenLastCalledWith('Moved 4 files to Anaconda.', 'ok', expect.objectContaining({ label: 'Undo' }));
    expect(screen.getByRole('list', { name: 'Suggestions' })).toBeInTheDocument();
    expect(row('Anaconda')).toHaveTextContent('AnacondaMoved4 files · 4.0 MBMay 2022 – Aug 2023');

    // A moved suggestion opened shows where its files are.
    await userEvent.click(row('Anaconda'));
    expect(screen.getByText('4 files are in Anaconda')).toBeInTheDocument();
    expect(screen.getByText('Photo Library › Anaconda')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Undo' }));
    expect(api.undoChange).toHaveBeenCalledWith('b1');
    expect(notify).toHaveBeenLastCalledWith('Put back. Every file is where it was.', 'ok');
  });

  it('says what could not be moved, what could not be put back, and what went wrong', async () => {
    api.applyOrganize.mockResolvedValueOnce(applied({ done: 3, skipped: 1, problems: ['a4.JPG is open elsewhere.'], batchId: '' }));
    const { notify } = show();
    await userEvent.click(row('Anaconda'));
    await userEvent.click(await screen.findByRole('button', { name: 'Move 4 files' }));
    expect(notify.mock.calls).toEqual([['Moved 3 files to Anaconda.', 'ok', undefined], ['1 left where it is. a4.JPG is open elsewhere.', 'error']]);

    notify.mockClear();
    api.applyOrganize.mockResolvedValueOnce(applied({ done: 0, skipped: 2, problems: [] })).mockRejectedValueOnce(new Error('You chose not to let PhotoSense change these photos.'));
    await userEvent.click(row('Uptown Butte'));
    await userEvent.click(await screen.findByRole('button', { name: 'Move 1 file' }));
    await userEvent.click(screen.getByRole('button', { name: 'Move 1 file' }));
    expect(notify.mock.calls).toEqual([['2 left where they are.', 'error'], ['You chose not to let PhotoSense change these photos.', 'error']]);
  });

  it('on a tablet shows the first suggestion beside the list, and what was done once it moves', async () => {
    api.undoChange.mockResolvedValueOnce({ restored: 1, skipped: 2, problems: ['Moved since.'] }).mockRejectedValueOnce('no answer');
    const { notify } = show({ tablet: true });
    expect(screen.getAllByRole('button', { name: 'Settings' })).toHaveLength(1);
    expect(row('Anaconda')).toHaveAttribute('aria-current', 'true');
    await userEvent.click(row('Uptown Butte'));
    await userEvent.click(await screen.findByRole('button', { name: 'Move 1 file' }));
    expect(screen.getByText('1 file is in Uptown Butte')).toBeInTheDocument();
    const undo = toastAction(notify);
    await act(async () => undo());
    expect(notify).toHaveBeenLastCalledWith('2 could not be put back. Moved since.', 'error');
    await act(async () => undo());
    expect(notify).toHaveBeenLastCalledWith('no answer', 'error');
  });

  it('on a tablet with nothing to sort says so beside the empty list', () => {
    files = [];
    show({ tablet: true });
    expect(screen.getByText('Nothing to sort: every photo is in an album, or has no location.')).toBeInTheDocument();
  });

  it('a move with nothing to undo offers no Undo in its done state', async () => {
    api.applyOrganize.mockImplementationOnce(async () => { files = files.filter(f => f.id !== 'b1'); changed(); return applied({ done: 1, batchId: '' }); });
    show({ tablet: true });
    await userEvent.click(row('Uptown Butte'));
    await userEvent.click(await screen.findByRole('button', { name: 'Move 1 file' }));
    expect(screen.getByText('1 file is in Uptown Butte')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Undo' })).not.toBeInTheDocument();
  });
});

describe('names already taken', () => {
  const there = (id: string, more = {}) => ({ id, name: 'a2.JPG', sizeBytes: 1024 ** 2, date: '2023-06-01T10:00:00', width: 1, height: 1, isVideo: false, identical: true, ...more });
  beforeEach(() => {
    taken = { [`${ROOT}/Anaconda/2023`]: ['a2.JPG', 'a4.JPG'] };
    clashes = [{ id: 'a2', existing: [there('x2')], nextFree: 'a2 (1).JPG' }, { id: 'a4', existing: [there('x4', { name: 'a4.JPG', identical: false, isVideo: true })], nextFree: 'a4 (1).JPG' }];
  });

  it('says how many and how each is named, goes through each, and the names chosen are the ones moved', async () => {
    const { notify } = show();
    await userEvent.click(row('Anaconda'));
    expect(await screen.findByText('2 names are already taken in Anaconda › 2023. Each gets a number, as in a2 (1).JPG.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Review each name' }));
    let names = sheet('Name taken');
    expect(names).toHaveTextContent('1 of 2 · The file going in and the one already there');
    expect(names).toHaveTextContent('From Photo Library');
    expect(names).toHaveTextContent('In Photo Library › Anaconda › 2023');
    expect(names).toHaveTextContent('Byte for byte the same file.');
    expect(within(names).getByRole('radio', { name: /^Add a number/ })).toHaveAttribute('aria-checked', 'true');
    await userEvent.click(within(names).getByRole('radio', { name: /^Give it another name/ }));
    const typed = within(names).getByRole('textbox', { name: 'New name' });
    expect(typed).toHaveValue('a2 2023-06');
    await userEvent.clear(typed);
    expect(within(names).getByRole('alert')).toHaveTextContent('That name is taken or has characters file names cannot use.');
    await userEvent.type(typed, 'Lake');
    // Chosen again, the name typed is kept.
    await userEvent.click(within(names).getByRole('radio', { name: /^Give it another name/ }));
    expect(within(names).getByRole('textbox', { name: 'New name' })).toHaveValue('Lake');
    await userEvent.click(within(names).getByRole('button', { name: 'Next name' }));
    names = sheet('Name taken');
    expect(names).toHaveTextContent('2 of 2');
    expect(names).toHaveTextContent('Two different files with the same name.');
    await userEvent.click(within(names).getByRole('radio', { name: /^Leave it where it is/ }));
    expect(within(names).getByRole('radio', { name: /^Leave it where it is/ })).toHaveTextContent('It stays in Photo Library');
    await userEvent.click(within(names).getByRole('radio', { name: /^Add a number/ }));
    await userEvent.click(within(names).getByRole('radio', { name: /^Leave it where it is/ }));
    await userEvent.click(within(names).getByRole('button', { name: 'Done' }));
    expect(notify).toHaveBeenLastCalledWith('2 names reviewed. Nothing moves until you tap Move.', 'ok');
    expect(screen.getByText('2 names reviewed.')).toBeInTheDocument();
    expect(tile('a4.JPG')).toHaveTextContent('Stays');
    await userEvent.click(screen.getByRole('button', { name: 'Move 3 files' }));
    expect(api.applyOrganize.mock.calls[0][0].files.map((f: { name: string }) => f.name)).toEqual(['a1.JPG', 'Lake.JPG', 'a3.JPG']);
  });

  it('puts the sheet away with Close, and says a single name as one', async () => {
    clashes = clashes.slice(0, 1);
    taken = { [`${ROOT}/Anaconda/2023`]: ['a2.JPG'] };
    show();
    await userEvent.click(row('Anaconda'));
    expect(await screen.findByText('1 name is already taken in Anaconda › 2023. It gets a number, as in a2 (1).JPG.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Review each name' }));
    await userEvent.click(within(sheet('Name taken')).getByRole('button', { name: 'Close' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Review each name' }));
    await userEvent.click(within(sheet('Name taken')).getByRole('button', { name: 'Done' }));
    expect(screen.getByText('1 name reviewed.')).toBeInTheDocument();
  });

  it('names the album when the names taken are in several of its years', async () => {
    taken = { [`${ROOT}/Anaconda/2022`]: ['a1.JPG'], [`${ROOT}/Anaconda/2023`]: ['a2.JPG'] };
    clashes = [{ id: 'a1', existing: [there('x1', { name: 'a1.JPG' })], nextFree: 'a1 (1).JPG' }, clashes[0]];
    show();
    await userEvent.click(row('Anaconda'));
    expect(await screen.findByText('2 names are already taken in Anaconda. Each gets a number, as in a1 (1).JPG.')).toBeInTheDocument();
  });

  it('deletes either file after a question naming the one that stays, with Undo; Cancel keeps both', async () => {
    const { notify } = show();
    await userEvent.click(row('Anaconda'));
    await userEvent.click(await screen.findByRole('button', { name: 'Review each name' }));
    const names = sheet('Name taken');
    await userEvent.click(within(names).getByRole('button', { name: 'Delete a2.JPG, already there' }));
    const asked = within(names).getByRole('alertdialog', { name: 'Delete a2.JPG?' });
    expect(asked).toHaveTextContent('Delete a2.JPG from Photo Library › Anaconda › 2023? The one in Photo Library stays.');
    await userEvent.click(within(asked).getByRole('button', { name: 'Cancel' }));
    expect(api.removeOrganize).not.toHaveBeenCalled();
    await userEvent.click(within(names).getByRole('button', { name: 'Delete a2.JPG, going in' }));
    await userEvent.click(within(sheet('Name taken')).getByRole('button', { name: 'Delete' }));
    expect(api.removeOrganize).toHaveBeenCalledWith(ROOT, ['a2']);
    expect(notify).toHaveBeenLastCalledWith('a2.JPG deleted from Photo Library. a2.JPG in Photo Library › Anaconda › 2023 stays.', 'ok', expect.objectContaining({ label: 'Undo' }));
    // No name is taken any longer, so the sheet has gone.
    expect(await screen.findByText('3 of 3 files will move. Tap a file to leave it where it is.')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('says when a file could not be deleted, or why', async () => {
    api.removeOrganize.mockResolvedValueOnce(applied({ done: 0, skipped: 1, problems: ['It is open.'] })).mockResolvedValueOnce(applied({ done: 0, skipped: 1, problems: [] })).mockRejectedValueOnce(new Error('Gone.'));
    const { notify } = show();
    await userEvent.click(row('Anaconda'));
    await userEvent.click(await screen.findByRole('button', { name: 'Review each name' }));
    for (const said of ['a2.JPG was left where it is. It is open.', 'a2.JPG was left where it is.', 'Gone.']) {
      await userEvent.click(within(sheet('Name taken')).getByRole('button', { name: 'Delete a2.JPG, going in' }));
      await userEvent.click(within(sheet('Name taken')).getByRole('button', { name: 'Delete' }));
      expect(notify).toHaveBeenLastCalledWith(said, 'error');
    }
  });

  it('names another file of the same move as also going in, and a deletion with nothing to undo offers no Undo', async () => {
    taken = {};
    files = [at('a1', { place: 0, date: '2023-01-01T00:00:00' }), at('dup', { name: 'a1.JPG', place: 0, date: '2023-02-01T00:00:00', folder: `${ROOT}/Other` })];
    files = [files[0], { ...files[1], folder: ROOT }];
    api.removeOrganize.mockResolvedValueOnce(applied({ done: 1, batchId: '' }));
    const { notify } = show();
    await userEvent.click(row('Anaconda'));
    await userEvent.click(await screen.findByRole('button', { name: 'Review each name' }));
    const names = sheet('Name taken');
    expect(names).toHaveTextContent('Also going in');
    expect(names).toHaveTextContent('Also from Photo Library');
    await userEvent.click(within(names).getByRole('button', { name: 'Delete a1.JPG, also going in' }));
    await userEvent.click(within(sheet('Name taken')).getByRole('button', { name: 'Delete' }));
    expect(notify).toHaveBeenLastCalledWith('a1.JPG deleted from Photo Library. a1.JPG in Photo Library stays.', 'ok', undefined);
  });
});

describe('your albums and what was moved', () => {
  it('makes a new album ready for files from Library, and lists those made, with their files once there are some', async () => {
    const made = [{ path: `${ROOT}/Lake`, label: 'Lake', base: ROOT }, { path: `${ROOT}/Trips/Montana`, label: 'Montana', base: `${ROOT}/Trips` }];
    const { onMade, notify } = show({ made });
    const yours = screen.getByRole('region', { name: 'Your albums' });
    expect(within(yours).getAllByRole('listitem').map(li => li.textContent)).toEqual(['LakeSelect files in Library, then Add to album.', 'Montana 1 file', 'New album']);
    await userEvent.click(within(yours).getByRole('button', { name: 'New album' }));
    await userEvent.type(within(sheet('New album')).getByRole('textbox', { name: 'Album name' }), 'Lake{Enter}');
    expect(within(sheet('New album')).getByRole('alert')).toHaveTextContent('You already have an album called Lake.');
    await userEvent.clear(within(sheet('New album')).getByRole('textbox', { name: 'Album name' }));
    await userEvent.type(within(sheet('New album')).getByRole('textbox', { name: 'Album name' }), 'Butte 2024{Enter}');
    expect(onMade).toHaveBeenCalledWith({ path: `${ROOT}/Butte 2024`, label: 'Butte 2024', base: ROOT });
    expect(notify).toHaveBeenLastCalledWith('Butte 2024 is ready. Select files in Library, then Add to album.', 'ok');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await userEvent.click(within(yours).getByRole('button', { name: 'New album' }));
    await userEvent.click(within(sheet('New album')).getByRole('button', { name: 'Done' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('lists what was moved lately, how long ago, each with Undo', async () => {
    batches = [
      { id: 'm1', label: 'Anaconda', mode: 'move', count: 724, bytes: 1, utc: new Date(Date.now() - 2 * 60_000).toISOString() },
      { id: 'r1', label: `${ROOT}/Trips`, mode: 'remove', count: 1, bytes: 1, utc: new Date(Date.now() - 10_000).toISOString() },
    ];
    api.undoChange.mockResolvedValueOnce({ restored: 0, skipped: 0, problems: [] });
    const { notify } = show();
    await userEvent.click(screen.getByRole('button', { name: 'Recently moved 2' }));
    const recent = sheet('Recently moved');
    expect(within(recent).getAllByRole('listitem').map(li => li.textContent)).toEqual(['Moved 724 files to Anaconda2 min agoUndo', 'Deleted 1 file from Photo Library › TripsJust nowUndo']);
    expect(recent).toHaveTextContent('Undo puts files back under the names they had. What was deleted permanently can\'t be put back.');
    await userEvent.click(within(recent).getAllByRole('button', { name: 'Undo' })[0]);
    expect(api.undoChange).toHaveBeenCalledWith('m1');
    // Nothing came back, and nothing was left behind: nothing to say.
    expect(notify).not.toHaveBeenCalled();
    await userEvent.click(within(recent).getByRole('button', { name: 'Done' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('says there is nothing to undo', async () => {
    api.useOrganizeBatches.mockReturnValue({});
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Recently moved 0' }));
    expect(sheet('Recently moved')).toHaveTextContent('Nothing to undo.');
  });
});

describe('Organize, the rest of the way', () => {
  it('on Android says folders where an iPhone says albums: reading, and when nothing is left to sort', async () => {
    files = files.map(f => ({ ...f, folder: f.folder.replace(ROOT, '/Phone') }));
    reading = { done: 1, total: 2 };
    const { notify } = show({ platform: 'android' });
    await userEvent.click(row('Anaconda'));
    expect(notify).toHaveBeenCalledWith('Still reading your photos. Preview a folder once every file is in.', 'info');
    reading = undefined;
    files = [];
    changed();
    expect(await screen.findByText('Every photo with a location is in a folder.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('radio', { name: 'Date' }));
    expect(screen.getByText('Every photo is in a folder.')).toBeInTheDocument();
  });

  it('on an Android tablet says there is nothing to sort, in its words; and draws the map larger', async () => {
    files = [];
    const view = show({ platform: 'android', tablet: true });
    expect(screen.getByText('Nothing to sort: every photo is in a folder, or has no location.')).toBeInTheDocument();
    view.unmount();
    files = [at('a1', { place: 0 })];
    show({ tablet: true });
    await userEvent.click(screen.getByRole('button', { name: 'Show the map' }));
    expect(screen.getByRole('group', { name: 'Map' })).toHaveClass('h-[420px]');
  });

  it('on a phone goes back from what was moved to the list', async () => {
    show();
    await userEvent.click(row('Uptown Butte'));
    await userEvent.click(await screen.findByRole('button', { name: 'Move 1 file' }));
    await userEvent.click(row('Uptown Butte'));
    expect(screen.getByText('1 file is in Uptown Butte')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(screen.getByRole('list', { name: 'Suggestions' })).toBeInTheDocument();
  });

  it('on the map shows the first suggestion once the one picked has moved', async () => {
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Show the map' }));
    await userEvent.click(within(screen.getByRole('group', { name: 'Map' })).getByRole('button', { name: 'Uptown Butte · 1 file' }));
    await userEvent.click(screen.getByRole('button', { name: 'Preview' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Move 1 file' }));
    expect(within(screen.getByRole('group', { name: 'Map' })).getAllByRole('button').map(d => d.getAttribute('aria-pressed'))).toEqual(['true']);
  });

  it('says a file could not be put back, with no reason given, when the service gives none', async () => {
    batches = [{ id: 'm1', label: 'Anaconda', mode: 'move', count: 1, bytes: 1, utc: new Date().toISOString() }];
    api.undoChange.mockResolvedValueOnce({ restored: 0, skipped: 1, problems: [] });
    const { notify } = show();
    await userEvent.click(screen.getByRole('button', { name: 'Recently moved 1' }));
    await userEvent.click(within(sheet('Recently moved')).getByRole('button', { name: 'Undo' }));
    expect(notify).toHaveBeenLastCalledWith('1 could not be put back.', 'error');
  });

  it('uses only the answer to the latest question of where the files go', async () => {
    const first = deferred<unknown>(), second = deferred<unknown>();
    api.planOrganize.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
    show();
    await userEvent.click(row('Uptown Butte'));
    await userEvent.type(screen.getByRole('textbox'), 's');
    await userEvent.type(screen.getByRole('textbox'), 'x');
    await act(async () => { first.resolve({ destination: 'old', exists: false, items: [], clashes: [], taken: {}, companions: 0 }); second.reject(new Error('Too late.')); });
    expect(screen.queryByText('Too late.')).not.toBeInTheDocument();
    expect(await screen.findByText('Into Photo Library › Uptown Buttesx. Undo puts them back.')).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Move 1 file' })).toBeEnabled();
  });
});
