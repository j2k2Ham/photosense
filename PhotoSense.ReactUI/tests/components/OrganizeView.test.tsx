import React from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { OrganizeView } from '../../components/organize/OrganizeView';
import { saveOrganizeRoot } from '../../lib/lastScan';
import type { OrganizeReading } from '../../lib/useOrganizeReading';
import type { OrganizeApplyDto, OrganizeApplyRequest, OrganizeBatchDto, OrganizeDestination, OrganizeFileDto, OrganizeFilesDto, OrganizePlanDto, OrganizePlanFile } from '../../types';
import { deferred } from '../fakeSignalR';
import { organizeFile as file, organizePlace as place } from '../fixtures';

const api = vi.hoisted(() => ({
  useOrganizeFiles: vi.fn(), useOrganizeBatches: vi.fn(), planOrganize: vi.fn(), applyOrganize: vi.fn(), removeOrganize: vi.fn(), undoOrganize: vi.fn(), openOrganizeFile: vi.fn(), browseFolders: vi.fn(),
}));
vi.mock('../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../lib/apiClient')>()), ...api }));
// What has been read so far of a folder that is still being read.
const soFar = vi.hoisted(() => vi.fn());
vi.mock('../../lib/useOrganizeReading', () => ({ useOrganizeReading: soFar }));

const ROOT = 'C:\\Users\\jamie\\Phone Pictures', OTHER = 'D:\\Backup\\Phone 2022';
const PLACES = [
  place({ town: 'Anaconda', latitude: 46.1283, longitude: -112.9423 }),
  place({ town: 'Butte', area: 'Uptown Butte', latitude: 46.0038, longitude: -112.5348 }),
];
const at = (id: string, placeIndex: number | null, changes: Partial<OrganizeFileDto> = {}) => file({ id, name: `${id}.JPG`, place: placeIndex, folder: ROOT, date: '2022-07-04T10:00:00', ...changes });
const FILES = [at('a1', 0), at('a2', 0), at('a3', 0), at('b1', 1, { date: '2023-12-31T10:00:00' }), at('b2', 1), at('n1', null), at('n2', null)];

let listings: Record<string, { data?: OrganizeFilesDto; error?: Error }>;
let batches: OrganizeBatchDto[];
const notify = vi.fn();
const listing = (root: string, files: OrganizeFileDto[] = FILES, fromScan = true): OrganizeFilesDto => ({ root, fromScan, places: PLACES, files });
const applied = (changes: Partial<OrganizeApplyDto> = {}): OrganizeApplyDto => ({ batchId: 'b1', done: 0, bytes: 0, companions: 0, renamed: 0, skipped: 0, problems: [], items: [], ...changes });

beforeEach(() => {
  for (const mock of Object.values(api)) mock.mockReset();
  notify.mockReset();
  listings = { [ROOT]: { data: listing(ROOT) } };
  batches = [];
  api.useOrganizeFiles.mockImplementation((root?: string) => (root ? listings[root] ?? {} : {}));
  api.useOrganizeBatches.mockImplementation(() => ({ data: batches }));
  soFar.mockReset();
  api.planOrganize.mockImplementation(async (destination: OrganizeDestination, ids: OrganizePlanFile[]): Promise<OrganizePlanDto> => {
    const folder = destination.direct ? destination.basePath : `${destination.basePath}\\${destination.folderName}`;
    return { destination: folder, exists: false, items: ids.map(f => ({ id: f.id, folder })), clashes: [], taken: {}, companions: 0 };
  });
  api.openOrganizeFile.mockResolvedValue(undefined);
  api.browseFolders.mockImplementation(async (path?: string) => (path ? { path, parent: null, folders: [{ name: 'Phone 2022', path: OTHER }] } : { path: null, parent: null, folders: [{ name: 'Backup', path: 'D:\\Backup' }] }));
});

function open(startRoot: string | null = ROOT) {
  const view = render(<OrganizeView startRoot={startRoot ?? undefined} notify={notify} />);
  // Stands in for the listing being fetched again: the page is drawn afresh from what the service now has.
  return { ...view, refresh: () => view.rerender(<OrganizeView startRoot={startRoot ?? undefined} notify={notify} />) };
}

const chip = () => screen.getByLabelText('Folder being organized');
const gallery = () => screen.getByRole('list', { name: 'Files' });
const tiles = () => within(gallery()).queryAllByRole('button');
const tile = (name: string) => within(gallery()).getByRole('button', { name: new RegExp(`${name.replace('.', '\\.')}`) });
const shownNames = () => tiles().map(t => t.getAttribute('title'));
const panel = () => screen.getByRole('complementary', { name: 'Suggested folders' });
const cards = () => within(panel()).getAllByRole('button', { name: /^Show the files for / }).map(c => c.textContent);
const bar = () => screen.getByRole('region', { name: 'Selection' });
const preview = () => screen.getByRole('dialog', { name: 'Preview' });
const moveButton = () => within(preview()).getByRole('button', { name: /^(Move|Copy) [\d,]+ files?$|^(Moving|Copying)…$/ });
const said = () => notify.mock.calls.map(c => [c[0], c[1]]);
const dragged = () => ({ dataTransfer: { setData: vi.fn(), effectAllowed: '' } });
const selected = () => tiles().filter(t => t.getAttribute('aria-pressed') === 'true').map(t => t.getAttribute('title'));

describe('choosing the folder', () => {
  it('starts with the folder last scanned, saying how many files it holds and that nothing had to be read', () => {
    open();
    expect(api.useOrganizeFiles).toHaveBeenLastCalledWith(ROOT);
    expect(chip()).toHaveTextContent('Phone Pictures · 7 files · from your last scan');
    expect(shownNames()).toEqual(['a1.JPG', 'a2.JPG', 'a3.JPG', 'b1.JPG', 'b2.JPG', 'n1.JPG', 'n2.JPG']);
    expect(screen.getByText('Click to select, Shift-click for a range, drag onto your folders. Nothing moves until you preview and confirm.')).toBeInTheDocument();
    expect(notify).not.toHaveBeenCalled();
  });

  it('prefers the folder chosen here before', () => {
    saveOrganizeRoot(OTHER);
    listings[OTHER] = { data: listing(OTHER, [at('x', 0, { folder: OTHER })], false) };
    open();
    expect(chip()).toHaveTextContent('Phone 2022 · 1 file · read just now');
  });

  it('asks for a folder when there is none to start with', async () => {
    open(null);
    expect(chip()).toHaveTextContent('No folder chosen yet');
    expect(screen.getByText('Choose a folder to organize')).toBeInTheDocument();
    expect(screen.queryByRole('complementary')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Choose a folder' }));
    expect(screen.getByRole('dialog', { name: 'Choose a folder to organize' })).toBeInTheDocument();
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('reads another folder when one is chosen, starting afresh, and says when it has', async () => {
    const { refresh } = open();
    await userEvent.click(tile('a1.JPG'));
    await userEvent.click(screen.getByRole('button', { name: 'Choose root folder' }));
    const dialog = screen.getByRole('dialog', { name: 'Choose a folder to organize' });
    await userEvent.click(await within(dialog).findByRole('button', { name: 'Phone 2022' }));
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Use this folder' })).toBeEnabled());
    await userEvent.click(within(dialog).getByRole('button', { name: 'Use this folder' }));

    // While it is being read: its name, no count yet, nothing selected any more, and an overlay that says how far it has got.
    expect(api.useOrganizeFiles).toHaveBeenLastCalledWith(OTHER);
    expect(chip()).toHaveTextContent(/^Phone 2022$/);
    expect(soFar).toHaveBeenLastCalledWith(OTHER, true);
    expect(screen.getByRole('status', { name: 'Reading Phone 2022' })).toHaveTextContent('Reading dates and places in Phone 2022…Looking through the folder…');
    soFar.mockImplementation((): OrganizeReading => ({ reading: true, total: 2600, done: 650 }));
    refresh();
    expect(screen.getByRole('status', { name: 'Reading Phone 2022' })).toHaveTextContent('650 of 2,600 files · 25%');
    expect(screen.queryByRole('region', { name: 'Selection' })).not.toBeInTheDocument();
    expect(panel()).not.toHaveTextContent('Every file with a location');
    expect(localStorage.getItem('photosense-organize-root')).toBe(OTHER);

    listings[OTHER] = { data: listing(OTHER, [at('x', 0, { folder: OTHER }), at('y', 0, { folder: OTHER })], false) };
    soFar.mockReset();
    refresh();
    expect(chip()).toHaveTextContent('Phone 2022 · 2 files · read just now');
    // The overlay goes, and the service is no longer asked how far it has got.
    expect(screen.queryByRole('status', { name: 'Reading Phone 2022' })).not.toBeInTheDocument();
    expect(soFar).toHaveBeenLastCalledWith(OTHER, false);
    expect(said()).toEqual([['Read dates and places for 2 files in Phone 2022. No scan was needed.', 'ok']]);
    refresh();
    expect(notify).toHaveBeenCalledOnce();
  });

  it('shows a folder\'s files as they are read, and holds back previews until every file is in', async () => {
    listings[ROOT] = {};
    const { refresh } = open();
    expect(screen.getByRole('status', { name: 'Reading Phone Pictures' }).className).toContain('absolute');
    expect(tiles()).toHaveLength(0);

    // Three files have been read, and are shown in the order they were read.
    soFar.mockImplementation((): OrganizeReading => ({ reading: true, total: 7, done: 3, listing: { root: ROOT, fromScan: false, places: PLACES, files: [FILES[1], FILES[5], FILES[3]] } }));
    refresh();
    expect(shownNames()).toEqual(['a2.JPG', 'n1.JPG', 'b1.JPG']);
    expect(chip()).toHaveTextContent('Phone Pictures · 3 of 7 files so far');
    // Nothing is laid over them: how far the reading has got is said in a strip above.
    const strip = within(screen.getByRole('region', { name: 'All files' })).getByRole('status', { name: 'Reading Phone Pictures' });
    expect(strip).toHaveTextContent('Still reading Phone Pictures…3 of 7 files · 42%');
    expect(strip.className).not.toContain('absolute');
    expect(cards()).toHaveLength(2);
    expect(panel()).not.toHaveTextContent('No location ·');

    // They can be selected; a folder cannot be previewed while more of its files may turn up.
    await userEvent.click(tile('n1.JPG'));
    await userEvent.click(within(panel()).getByRole('button', { name: 'Preview Anaconda' }));
    expect(said()).toEqual([['Still reading Phone Pictures. Preview a folder once every file is in.', 'info']]);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    // The whole list comes, in its own order: what was selected still is.
    listings[ROOT] = { data: listing(ROOT) };
    soFar.mockReset();
    refresh();
    expect(shownNames()).toEqual(['a1.JPG', 'a2.JPG', 'a3.JPG', 'b1.JPG', 'b2.JPG', 'n1.JPG', 'n2.JPG']);
    expect(chip()).toHaveTextContent('Phone Pictures · 7 files · from your last scan');
    expect(screen.queryByRole('status', { name: 'Reading Phone Pictures' })).not.toBeInTheDocument();
    expect(selected()).toEqual(['n1.JPG']);
    // A range is not counted from a click made in the other order.
    fireEvent.click(tile('b2.JPG'), { shiftKey: true });
    expect(selected()).toEqual(['b2.JPG', 'n1.JPG']);
    expect(notify).toHaveBeenCalledOnce();
    await userEvent.click(within(panel()).getByRole('button', { name: 'Preview Anaconda' }));
    expect(preview()).toBeInTheDocument();
  });

  it('says so when the folder cannot be read', () => {
    listings[ROOT] = { error: new Error('Folder not found: C:\\Users\\jamie\\Phone Pictures') };
    const { refresh } = open();
    expect(said()).toEqual([['Folder not found: C:\\Users\\jamie\\Phone Pictures', 'error']]);
    expect(screen.getByText('This folder could not be read.')).toBeInTheDocument();
    expect(screen.queryByRole('status', { name: /^Reading/ })).not.toBeInTheDocument();
    listings[ROOT] = { error: new TypeError('Failed to fetch') };
    refresh();
    expect(said()[1]).toEqual(['Cannot reach the PhotoSense server.', 'error']);
    listings[ROOT] = { error: 'refused' as unknown as Error };
    refresh();
    expect(said()[2]).toEqual(['refused', 'error']);
  });
});

describe('looking through the files', () => {
  it('suggests a folder for each place, and counts what has no place', () => {
    open();
    expect(cards()).toEqual([
      'Anaconda3 files · 17.4 MBJul 2022Inside: a folder for each yearNo landmark known here, so it is named after the town',
      'Uptown Butte2 files · 11.6 MBJul 2022 – Dec 2023Inside: a folder for each year',
    ]);
    expect(panel()).toHaveTextContent('No location · 2 files');
    expect(within(panel()).getByRole('group', { name: 'Map of the suggested folders' })).toBeInTheDocument();
    expect(within(screen.getByRole('group', { name: 'Show' })).getAllByRole('button').map(b => b.textContent)).toEqual(['All7', 'Not organized7', 'No location2']);
  });

  it('searches by name, place or month, and says when nothing matches', async () => {
    open();
    const search = screen.getByRole('textbox', { name: 'Search by name, place or month' });
    await userEvent.type(search, 'BUTTE ');
    expect(shownNames()).toEqual(['b1.JPG', 'b2.JPG']);
    await userEvent.clear(search);
    await userEvent.type(search, 'december');
    expect(shownNames()).toEqual(['b1.JPG']);
    await userEvent.clear(search);
    await userEvent.type(search, ' zz ');
    expect(screen.getByText('No files match “zz”')).toBeInTheDocument();
    await userEvent.clear(search);
    expect(tiles()).toHaveLength(7);
  });

  it('shows only the files without a location, or only those not yet organized', async () => {
    open();
    await userEvent.click(within(screen.getByRole('group', { name: 'Show' })).getByRole('button', { name: /^No location/ }));
    expect(shownNames()).toEqual(['n1.JPG', 'n2.JPG']);
    await userEvent.click(within(screen.getByRole('group', { name: 'Show' })).getByRole('button', { name: /^Not organized/ }));
    expect(tiles()).toHaveLength(7);
  });

  it('goes back to every file with Clear all, whatever was keeping some from showing', async () => {
    open();
    const clearAll = () => screen.queryByRole('button', { name: 'Clear all' });
    const search = screen.getByRole('textbox', { name: 'Search by name, place or month' });
    const filters = () => within(screen.getByRole('group', { name: 'Show' })).getAllByRole('button').map(b => b.getAttribute('aria-pressed'));
    expect(clearAll()).not.toBeInTheDocument();

    // A folder picked from its card, from the map, or the files with no location: each on its own is something to clear.
    for (const pick of ['Show the files for Uptown Butte', 'Anaconda · 3 files', 'Show the files with no location']) {
      await userEvent.click(within(panel()).getByRole('button', { name: pick }));
      expect(tiles().length).toBeLessThan(7);
      await userEvent.click(clearAll()!);
      expect(tiles()).toHaveLength(7);
      expect(clearAll()).not.toBeInTheDocument();
    }
    // So is a search, even one of spaces only when it has come to nothing; and so is a filter.
    await userEvent.type(search, 'butte');
    await userEvent.click(clearAll()!);
    expect(search).toHaveValue('');
    await userEvent.type(search, '  ');
    expect(clearAll()).not.toBeInTheDocument();
    await userEvent.clear(search);
    await userEvent.click(within(screen.getByRole('group', { name: 'Show' })).getByRole('button', { name: /^No location/ }));
    await userEvent.click(clearAll()!);
    expect(filters()).toEqual(['true', 'false', 'false']);

    // All three at once go at once, and the files are shown from their first page.
    await userEvent.click(within(panel()).getByRole('button', { name: 'Show the files for Uptown Butte' }));
    await userEvent.type(search, 'december');
    await userEvent.click(within(screen.getByRole('group', { name: 'Show' })).getByRole('button', { name: /^Not organized/ }));
    expect(shownNames()).toEqual(['b1.JPG']);
    await userEvent.click(clearAll()!);
    expect(tiles()).toHaveLength(7);
    expect(search).toHaveValue('');
    expect(filters()).toEqual(['true', 'false', 'false']);
    expect(screen.queryByText('Suggested: Uptown Butte')).not.toBeInTheDocument();
    expect(within(panel()).getByRole('button', { name: 'Show the files for Uptown Butte' })).toHaveAttribute('aria-pressed', 'false');
    // What was selected is left as it was: clearing is about what is shown.
  });

  it('shows one folder\'s files alone when it is picked, from its card, the map or the no-location card', async () => {
    open();
    await userEvent.click(within(panel()).getByRole('button', { name: 'Show the files for Uptown Butte' }));
    expect(shownNames()).toEqual(['b1.JPG', 'b2.JPG']);
    expect(screen.getByText('Suggested: Uptown Butte')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Show every folder\'s files again' }));
    expect(tiles()).toHaveLength(7);

    await userEvent.click(within(panel()).getByRole('button', { name: 'Anaconda · 3 files' }));
    expect(shownNames()).toEqual(['a1.JPG', 'a2.JPG', 'a3.JPG']);
    await userEvent.click(within(panel()).getByRole('button', { name: 'Show the files with no location' }));
    expect(shownNames()).toEqual(['n1.JPG', 'n2.JPG']);
    expect(screen.getByText('No location', { selector: 'span.truncate' })).toBeInTheDocument();

    // Grouping another way makes other folders, so none is picked any more.
    await userEvent.click(within(panel()).getByRole('button', { name: 'Group by date' }));
    expect(tiles()).toHaveLength(7);
    expect(cards()).toEqual(['20231 file · 5.8 MBDec 2023', '20226 files · 34.9 MBJul 2022']);
  });

  it('follows the ways of grouping and naming, and hides the map when asked', async () => {
    open();
    await userEvent.click(within(panel()).getByRole('button', { name: 'Show the files for Anaconda' }));
    // How the folders are laid out inside does not change which folders there are: the one picked stays picked.
    await userEvent.click(within(within(panel()).getByRole('group', { name: 'Then by' })).getByRole('button', { name: 'Nothing' }));
    expect(screen.getByText('Suggested: Anaconda')).toBeInTheDocument();
    await userEvent.click(within(panel()).getByRole('button', { name: 'Folder names' }));
    await userEvent.click(screen.getByRole('option', { name: /^Town, state/ }));
    expect(cards().map(c => c!.slice(0, 17))).toEqual(['Anaconda, Montana', 'Butte, Montana2 f']);
    expect(screen.getByText('Suggested: Anaconda, Montana')).toBeInTheDocument();
    await userEvent.click(within(within(panel()).getByRole('group', { name: 'Nearby places' })).getByRole('button', { name: 'Keep separate' }));
    expect(screen.queryByText(/^Suggested:/)).not.toBeInTheDocument();
    await userEvent.click(within(panel()).getByRole('switch'));
    expect(within(panel()).queryByRole('group', { name: 'Map of the suggested folders' })).not.toBeInTheDocument();
  });

  it('turns pages of sixty, going back to the first when what is shown changes', async () => {
    const many = Array.from({ length: 130 }, (_, i) => at(`f${String(i).padStart(3, '0')}`, i < 70 ? 0 : null));
    listings[ROOT] = { data: listing(ROOT, many) };
    open();
    await userEvent.click(screen.getByRole('button', { name: 'Next' }));
    await userEvent.click(screen.getByRole('button', { name: 'Next' }));
    expect(screen.getByText('Page 3 of 3')).toBeInTheDocument();
    expect(tiles()).toHaveLength(10);
    await userEvent.click(within(screen.getByRole('group', { name: 'Show' })).getByRole('button', { name: /^No location/ }));
    expect(screen.getByText('Page 1 of 1')).toBeInTheDocument();
    expect(tiles()).toHaveLength(60);
  });

  it('opens a file in the default viewer on a double click, and says so when that fails', async () => {
    open();
    fireEvent.doubleClick(tile('b1.JPG'));
    expect(api.openOrganizeFile).toHaveBeenCalledExactlyOnceWith('b1');
    api.openOrganizeFile.mockRejectedValue(new Error('No application could be started for b1.JPG'));
    fireEvent.doubleClick(tile('b1.JPG'));
    await waitFor(() => expect(said()).toEqual([['No application could be started for b1.JPG', 'error']]));
  });
});

describe('selecting files and making folders', () => {
  it('selects with a click, a range with Shift, everything shown at once, and lets go again', async () => {
    open();
    expect(screen.queryByRole('region', { name: 'Selection' })).not.toBeInTheDocument();
    await userEvent.click(tile('a2.JPG'));
    fireEvent.click(tile('b1.JPG'), { shiftKey: true });
    expect(selected()).toEqual(['a2.JPG', 'a3.JPG', 'b1.JPG']);
    expect(bar()).toHaveTextContent('3 selectedSelect all 7 shown');
    // A range can be taken backwards too, and a click on a selected file lets go of it.
    fireEvent.click(tile('a1.JPG'), { shiftKey: true });
    await userEvent.click(tile('a3.JPG'));
    expect(selected()).toEqual(['a1.JPG', 'a2.JPG', 'b1.JPG']);
    await userEvent.click(within(bar()).getByRole('button', { name: 'Select all 7 shown' }));
    expect(selected()).toHaveLength(7);
    await userEvent.click(within(bar()).getByRole('button', { name: 'Clear' }));
    expect(selected()).toEqual([]);
    // With nothing clicked before it, Shift selects just the one.
    fireEvent.click(tile('n1.JPG'), { shiftKey: true });
    expect(selected()).toEqual(['n1.JPG']);
  });

  it('makes a folder of the selection from the bar, tags its files, and leaves them out of the suggestions', async () => {
    open();
    await userEvent.click(tile('a1.JPG'));
    await userEvent.click(tile('b1.JPG'));
    await userEvent.click(within(bar()).getByRole('button', { name: 'New folder from these' }));
    await userEvent.type(within(bar()).getByRole('textbox'), 'Trips\\Glacier 2022');
    await userEvent.click(within(bar()).getByRole('button', { name: 'Create folder' }));

    expect(said()).toEqual([['Made Trips\\Glacier 2022 with 2 files. Preview it when you are ready.', 'ok']]);
    expect(screen.queryByRole('region', { name: 'Selection' })).not.toBeInTheDocument();
    expect(within(panel()).getByRole('button', { name: 'Show the files for Trips\\Glacier 2022' })).toHaveTextContent('2 files · 11.6 MB · not moved yet');
    expect(tile('a1.JPG')).toHaveTextContent('For Trips\\Glacier 2022');
    expect(cards().slice(0, 2)).toEqual([
      'Anaconda2 files · 11.6 MBJul 2022Inside: a folder for each yearNo landmark known here, so it is named after the town', 'Uptown Butte1 file · 5.8 MBJul 2022Inside: a folder for each year',
    ]);

    // Its files alone, on a click; and more files put in it from a selection.
    await userEvent.click(within(panel()).getByRole('button', { name: 'Show the files for Trips\\Glacier 2022' }));
    expect(shownNames()).toEqual(['a1.JPG', 'b1.JPG']);
    expect(screen.getByText('Your folder: Trips\\Glacier 2022')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Show every folder\'s files again' }));
    await userEvent.click(tile('n1.JPG'));
    expect(bar()).toHaveTextContent('or add them to one of your folders');
    await userEvent.click(within(panel()).getByRole('button', { name: 'Add 1 here' }));
    expect(said()[1]).toEqual(['Added 1 file to Trips\\Glacier 2022. Preview it when you are ready.', 'ok']);
    expect(tile('n1.JPG')).toHaveTextContent('For Trips\\Glacier 2022');
    expect(panel()).toHaveTextContent('No location · 1 file');
  });

  it('refuses a folder name that cannot be used, and one that is already taken', async () => {
    open();
    await userEvent.click(within(panel()).getByRole('button', { name: 'New folder' }));
    const name = within(panel()).getByRole('textbox', { name: 'Folder name' });
    fireEvent.keyDown(name, { key: 'Enter' });
    await userEvent.type(name, 'What?{Enter}');
    await userEvent.clear(name);
    await userEvent.type(name, 'Trips{Enter}');
    expect(said()).toEqual([['Give the new folder a name.', 'error'], ['Folder names cannot contain < > : " / | ? or *', 'error'], ['Made Trips. Select or drag files onto it.', 'ok']]);
    expect(within(panel()).queryByRole('textbox', { name: 'Folder name' })).not.toBeInTheDocument();

    await userEvent.click(within(panel()).getByRole('button', { name: 'New folder' }));
    await userEvent.type(within(panel()).getByRole('textbox', { name: 'Folder name' }), 'trips{Enter}');
    expect(said()[3]).toEqual(['You already have a folder called trips.', 'error']);
    await userEvent.click(within(panel()).getByRole('button', { name: 'Cancel' }));
    expect(within(panel()).queryByRole('textbox', { name: 'Folder name' })).not.toBeInTheDocument();
    // The name that was being typed is not kept for next time.
    await userEvent.click(within(panel()).getByRole('button', { name: 'New folder' }));
    expect(within(panel()).getByRole('textbox', { name: 'Folder name' })).toHaveValue('');
  });

  it('gives up naming a folder from the bar on Cancel, or when the selection is let go', async () => {
    open();
    await userEvent.click(tile('a1.JPG'));
    await userEvent.click(within(bar()).getByRole('button', { name: 'New folder from these' }));
    await userEvent.type(within(bar()).getByRole('textbox'), 'Half a name');
    await userEvent.click(within(bar()).getByRole('button', { name: 'Cancel' }));
    expect(within(bar()).getByRole('button', { name: 'New folder from these' })).toBeInTheDocument();
    await userEvent.click(within(bar()).getByRole('button', { name: 'New folder from these' }));
    expect(within(bar()).getByRole('textbox')).toHaveValue('');
  });

  it('removes a folder of your own, giving its files back to the suggestions', async () => {
    open();
    await userEvent.click(tile('a1.JPG'));
    await userEvent.click(within(bar()).getByRole('button', { name: 'New folder from these' }));
    await userEvent.type(within(bar()).getByRole('textbox'), 'Trips{Enter}');
    await userEvent.click(within(panel()).getByRole('button', { name: 'New folder' }));
    await userEvent.type(within(panel()).getByRole('textbox', { name: 'Folder name' }), 'Family{Enter}');
    await userEvent.click(within(panel()).getByRole('button', { name: 'Show the files for Trips' }));
    expect(shownNames()).toEqual(['a1.JPG']);

    // Removing another folder leaves the one that is picked picked; removing that one shows every file again.
    await userEvent.click(within(panel()).getByRole('button', { name: 'Remove Family' }));
    expect(screen.getByText('Your folder: Trips')).toBeInTheDocument();
    await userEvent.click(within(panel()).getByRole('button', { name: 'Remove Trips' }));
    expect(said().slice(2)).toEqual([['Removed Family. Its files go back into the suggestions.', 'ok'], ['Removed Trips. Its files go back into the suggestions.', 'ok']]);
    expect(tiles()).toHaveLength(7);
    expect(tile('a1.JPG')).not.toHaveTextContent('For Trips');
    expect(cards()[0]).toMatch(/^Anaconda3 files/);
  });

  it('removes a folder while another kind of folder is picked without letting go of that', async () => {
    open();
    await userEvent.click(within(panel()).getByRole('button', { name: 'New folder' }));
    await userEvent.type(within(panel()).getByRole('textbox', { name: 'Folder name' }), 'Family{Enter}');
    await userEvent.click(within(panel()).getByRole('button', { name: 'Show the files for Anaconda' }));
    await userEvent.click(within(panel()).getByRole('button', { name: 'Remove Family' }));
    expect(screen.getByText('Suggested: Anaconda')).toBeInTheDocument();
  });
});

describe('dragging files', () => {
  it('puts one file, or the whole selection, into the folder it is dropped on', async () => {
    open();
    await userEvent.click(within(panel()).getByRole('button', { name: 'New folder' }));
    await userEvent.type(within(panel()).getByRole('textbox', { name: 'Folder name' }), 'Trips{Enter}');
    const folder = () => within(panel()).getByRole('button', { name: 'Show the files for Trips' }).parentElement!;

    // A file that is not part of the selection goes alone.
    await userEvent.click(tile('a1.JPG'));
    fireEvent.dragStart(tile('n1.JPG'), dragged());
    expect(within(panel()).getByRole('group', { name: 'Drop the files on a folder' })).toHaveTextContent('Drop 1 file on one of your folders');
    fireEvent.drop(folder());
    expect(said()[1]).toEqual(['Added 1 file to Trips. Preview it when you are ready.', 'ok']);
    expect(within(panel()).queryByRole('group', { name: 'Drop the files on a folder' })).not.toBeInTheDocument();

    // One of the selected files takes the others with it.
    await userEvent.click(tile('a1.JPG'));
    await userEvent.click(tile('a2.JPG'));
    fireEvent.dragStart(tile('a2.JPG'), dragged());
    expect(within(panel()).getByRole('group', { name: 'Drop the files on a folder' })).toHaveTextContent('Drop 2 files');
    fireEvent.drop(folder());
    expect(said()[2]).toEqual(['Added 2 files to Trips. Preview it when you are ready.', 'ok']);
    expect(folder()).toHaveTextContent('3 files');
  });

  it('adds a file to the suggested folder it is dropped on, taking it out of a folder of your own', async () => {
    open();
    await userEvent.click(tile('n1.JPG'));
    await userEvent.click(within(bar()).getByRole('button', { name: 'New folder from these' }));
    await userEvent.type(within(bar()).getByRole('textbox'), 'Trips{Enter}');
    fireEvent.dragStart(tile('n1.JPG'), dragged());
    fireEvent.drop(within(panel()).getByRole('button', { name: 'Show the files for Uptown Butte' }).parentElement!);
    expect(said()[1]).toEqual(['Added 1 file to the suggested folder Uptown Butte', 'ok']);
    expect(tile('n1.JPG')).toHaveTextContent('For Uptown Butte');
    expect(within(panel()).getByRole('button', { name: 'Show the files for Uptown Butte' })).toHaveTextContent(/^Uptown Butte3 files/);
    expect(within(panel()).getByRole('button', { name: 'Show the files for Trips' })).toHaveTextContent('Empty. Select or drag files onto it.');
  });

  it('starts a new folder from what is dropped on "New folder", and forgets a drag that ends nowhere', async () => {
    open();
    fireEvent.dragStart(tile('b2.JPG'), dragged());
    fireEvent.dragEnd(tile('b2.JPG'));
    expect(within(panel()).queryByRole('group', { name: 'Drop the files on a folder' })).not.toBeInTheDocument();

    fireEvent.dragStart(tile('b2.JPG'), dragged());
    fireEvent.drop(within(panel()).getByRole('group', { name: 'Drop the files on a folder' }).querySelector('[data-drop="New folder"]')!);
    expect(selected()).toEqual(['b2.JPG']);
    expect(within(bar()).getByRole('textbox', { name: 'Name of the new folder' })).toBeInTheDocument();
    // Letting the selection go from here gives the naming up too.
    await userEvent.type(within(bar()).getByRole('textbox'), 'x');
    await userEvent.click(within(bar()).getByRole('button', { name: 'Cancel' }));
    await userEvent.click(within(bar()).getByRole('button', { name: 'Clear' }));
    expect(screen.queryByRole('region', { name: 'Selection' })).not.toBeInTheDocument();
  });
});

describe('grouping by two things', () => {
  it('makes a folder for each year with a folder for each place inside, and keeps the picked year picked as that changes', async () => {
    open();
    await userEvent.click(within(within(panel()).getByRole('group', { name: 'Group by' })).getByRole('button', { name: 'Date' }));
    await userEvent.click(within(panel()).getByRole('button', { name: 'Show the files for 2022' }));
    await userEvent.click(within(within(panel()).getByRole('group', { name: 'Then by' })).getByRole('button', { name: 'Place' }));
    expect(screen.getByText('Suggested: 2022')).toBeInTheDocument();
    expect(cards()).toEqual(['20231 file · 5.8 MBDec 2023Inside: a folder for each place', '20226 files · 34.9 MBJul 2022Inside: a folder for each place']);

    await userEvent.click(within(panel()).getByRole('button', { name: 'Preview 2022' }));
    await waitFor(() => expect(moveButton()).toBeEnabled());
    expect(api.planOrganize).toHaveBeenLastCalledWith({ basePath: ROOT, folderName: '2022', direct: false },
      [{ id: 'a1', subfolder: 'Anaconda' }, { id: 'a2', subfolder: 'Anaconda' }, { id: 'a3', subfolder: 'Anaconda' }, { id: 'b2', subfolder: 'Uptown Butte' }, { id: 'n1' }, { id: 'n2' }]);
    expect(preview()).toHaveTextContent('Split into a subfolder for each place: Anaconda, Uptown Butte. 2 files with no location go into the folder itself.');
  });
});

describe('deleting files', () => {
  const bin = (count: number) => within(bar()).getByRole('button', { name: `Delete the ${count} selected ${count === 1 ? 'file' : 'files'}` });
  const question = () => screen.getByRole('alertdialog');
  const held = (...ids: string[]) => ids.map(id => ({ id, path: `${ROOT}\\_PhotoSense_Removed\\${id}.JPG` }));

  it('asks first, then takes the files off the page at once and offers to put them back', async () => {
    api.removeOrganize.mockResolvedValue(applied({ batchId: 'r1', done: 2, bytes: 12_186_000, companions: 1, items: held('a1', 'n1') }));
    api.undoOrganize.mockResolvedValue({ restored: 2, skipped: 0, problems: [] });
    open();
    await userEvent.click(tile('a1.JPG'));
    await userEvent.click(tile('n1.JPG'));
    await userEvent.click(bin(2));
    expect(question()).toHaveAccessibleName('Delete 2 files?');
    expect(question()).toHaveTextContent('2 files taking 11.6 MB will be removed from Phone Pictures, along with any Live Photo videos and edit files that belong only to them.');
    expect(question()).toHaveTextContent('Nothing is erased. The files are moved to _PhotoSense_Removed inside Phone Pictures, where Clean up puts what it removes. Undo this from Recently moved, or use Delete permanently in the menu to free the space.');
    // Nothing goes until it is confirmed, and nothing at all on Cancel.
    expect(api.removeOrganize).not.toHaveBeenCalled();
    await userEvent.click(within(question()).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(selected()).toEqual(['a1.JPG', 'n1.JPG']);

    await userEvent.click(bin(2));
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 2 files' }));
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    expect(api.removeOrganize).toHaveBeenCalledExactlyOnceWith(ROOT, ['a1', 'n1']);
    expect(notify).toHaveBeenLastCalledWith('Moved 2 files (11.6 MB) to _PhotoSense_Removed in Phone Pictures, with 1 Live Photo and edit files', 'ok', { label: 'Undo', run: expect.any(Function) });
    expect(shownNames()).toEqual(['a2.JPG', 'a3.JPG', 'b1.JPG', 'b2.JPG', 'n2.JPG']);
    expect(chip()).toHaveTextContent('Phone Pictures · 5 files');
    expect(screen.queryByRole('region', { name: 'Selection' })).not.toBeInTheDocument();
    expect(cards()[0]).toMatch(/^Anaconda2 files/);

    // Undone from the message, they are back.
    const undo = notify.mock.calls[notify.mock.calls.length - 1][2] as { run(): void };
    await act(async () => undo.run());
    expect(api.undoOrganize).toHaveBeenCalledExactlyOnceWith('r1');
    expect(notify).toHaveBeenLastCalledWith('Moved 2 files back to where they were', 'ok');
    expect(shownNames()).toHaveLength(7);
  });

  it('names a single file, and deletes it without a word about linked files when it had none', async () => {
    api.removeOrganize.mockResolvedValue(applied({ batchId: 'r2', done: 1, bytes: 6_093_000, items: held('b1') }));
    open();
    await userEvent.click(tile('b1.JPG'));
    await userEvent.click(bin(1));
    expect(question()).toHaveAccessibleName('Delete b1.JPG?');
    expect(question()).toHaveTextContent('1 file taking 5.8 MB will be removed from Phone Pictures');
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 1 file' }));
    await waitFor(() => expect(notify).toHaveBeenLastCalledWith('Moved 1 file (5.8 MB) to _PhotoSense_Removed in Phone Pictures', 'ok', expect.anything()));
    expect(shownNames()).not.toContain('b1.JPG');
  });

  it('says what could not be deleted, and why the service could not be asked', async () => {
    api.removeOrganize.mockResolvedValueOnce(applied({ batchId: '', skipped: 1, problems: ['a1.JPG: The process cannot access the file because it is being used by another process.'] }));
    open();
    await userEvent.click(tile('a1.JPG'));
    await userEvent.click(bin(1));
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 1 file' }));
    await waitFor(() => expect(said()).toEqual([['1 left where it is. a1.JPG: The process cannot access the file because it is being used by another process.', 'error']]));
    // Still there, and still selected.
    expect(selected()).toEqual(['a1.JPG']);

    api.removeOrganize.mockResolvedValueOnce(applied({ batchId: '', skipped: 2 }));
    await userEvent.click(tile('a2.JPG'));
    await userEvent.click(bin(2));
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 2 files' }));
    await waitFor(() => expect(said()[1]).toEqual(['2 left where they are.', 'error']));

    // The question stays open when the service cannot be reached, so that it can be asked again.
    api.removeOrganize.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    await userEvent.click(bin(2));
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 2 files' }));
    await waitFor(() => expect(said()[2]).toEqual(['Cannot reach the PhotoSense server.', 'error']));
    expect(within(question()).getByRole('button', { name: 'Delete 2 files' })).toBeEnabled();
  });

  describe('from the window of names already taken', () => {
    const clashWindow = () => screen.getByRole('dialog', { name: /^Name already taken/ });
    const noClashWindow = () => waitFor(() => expect(screen.queryByRole('dialog', { name: /^Name already taken/ })).not.toBeInTheDocument());
    /** What the service plans while a file called a1.JPG is in the folder the files are going to: the very same file as the one arriving. */
    const nameTaken = () => api.planOrganize.mockImplementation(async (destination: OrganizeDestination, ids: OrganizePlanFile[]): Promise<OrganizePlanDto> => {
      const folder = `${destination.basePath}\\${destination.folderName}`;
      return {
        destination: folder, exists: true, items: ids.map(f => ({ id: f.id, folder })), companions: 0, taken: { [folder]: ['a1.JPG'] },
        clashes: [{ id: 'a1', nextFree: 'a1 (1).JPG', existing: [{ id: 'x1', name: 'a1.JPG', sizeBytes: 2_097_152, date: '2022-07-04T10:00:00', width: 4032, height: 3024, isVideo: false, placeName: null, identical: true }] }],
      };
    });
    const nameFree = api.planOrganize.getMockImplementation.bind(api.planOrganize);
    const goes = () => api.removeOrganize.mockImplementation(async (_root: string, ids: string[]) => applied({ batchId: 'r3', done: 1, bytes: 2_097_152, items: held(...ids) }));
    /** Opens the folder's preview and, from it, the window that goes through the names. */
    async function review(folder: string) {
      await userEvent.click(within(panel()).getByRole('button', { name: `Preview ${folder}` }));
      await userEvent.click(await within(preview()).findByRole('button', { name: 'Review each name' }));
    }
    const bin = (name: string) => within(clashWindow()).getByRole('button', { name });

    it('deletes the file that has the name, saying which of the two stays, and goes on once it has gone', async () => {
      const free = nameFree()!;
      nameTaken();
      goes();
      open();
      await review('Anaconda');
      await userEvent.click(bin('Delete a1.JPG, already there'));
      expect(question()).toHaveAccessibleName('Delete a1.JPG?');
      expect(question()).toHaveTextContent('a1.JPG (2.0 MB) in Anaconda will be removed, along with any Live Photo video and edit files that belong only to it.');
      expect(question()).toHaveTextContent('a1.JPG in Top level stays.');
      expect(question()).toHaveTextContent('Undo this from Recently moved, or use Delete permanently in the menu to free the space.');
      // Esc answers the question and nothing else: both windows behind it stay.
      fireEvent.keyDown(window, { key: 'Escape' });
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
      expect(clashWindow()).toBeInTheDocument();
      expect(preview()).toBeInTheDocument();
      expect(api.removeOrganize).not.toHaveBeenCalled();

      await userEvent.click(bin('Delete a1.JPG, already there'));
      // With that file gone, nothing in the folder has the name.
      api.planOrganize.mockImplementation(free);
      const plans = api.planOrganize.mock.calls.length;
      await userEvent.click(within(question()).getByRole('button', { name: 'Delete 1 file' }));
      await noClashWindow();
      expect(api.removeOrganize).toHaveBeenCalledExactlyOnceWith(ROOT, ['x1']);
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
      // What is there was asked about again, and the file that was moving still is.
      expect(api.planOrganize.mock.calls.length).toBeGreaterThan(plans);
      expect(notify).toHaveBeenLastCalledWith('Moved 1 file (2.0 MB) to _PhotoSense_Removed in Phone Pictures', 'ok', { label: 'Undo', run: expect.any(Function) });
      await waitFor(() => expect(moveButton()).toBeEnabled());
      expect(preview()).toHaveTextContent('3 files will move');
      expect(preview()).not.toHaveTextContent('Name already taken');
    });

    it('deletes the file being moved instead, which leaves the preview and the page', async () => {
      nameTaken();
      goes();
      open();
      await userEvent.click(tile('a1.JPG'));
      await userEvent.click(tile('n1.JPG'));
      await review('Anaconda');
      await userEvent.click(bin('Delete a1.JPG, moving'));
      expect(question()).toHaveTextContent('a1.JPG (5.8 MB) in Top level will be removed');
      expect(question()).toHaveTextContent('a1.JPG in Anaconda stays.');
      await userEvent.click(within(question()).getByRole('button', { name: 'Delete 1 file' }));
      // No other file of the folder has a name that is taken, so there is nothing left to go through.
      await noClashWindow();
      expect(api.removeOrganize).toHaveBeenCalledExactlyOnceWith(ROOT, ['a1']);
      await waitFor(() => expect(moveButton()).toBeEnabled());
      expect(preview()).toHaveTextContent('2 files will move');
      await userEvent.click(within(preview()).getByRole('button', { name: 'Cancel' }));
      expect(shownNames()).toEqual(['a2.JPG', 'a3.JPG', 'b1.JPG', 'b2.JPG', 'n1.JPG', 'n2.JPG']);
      // It is no longer among the selected, and what else was selected still is.
      expect(bar()).toHaveTextContent('1 selected');
      expect(selected()).toEqual(['n1.JPG']);
    });

    it('closes the preview of a folder whose last file was deleted, and does not open it again when the file is put back', async () => {
      listings[ROOT] = { data: listing(ROOT, [at('a1', 0), at('b1', 1), at('b2', 1)]) };
      nameTaken();
      goes();
      api.undoOrganize.mockResolvedValue({ restored: 1, skipped: 0, problems: [] });
      open();
      await review('Anaconda');
      await userEvent.click(bin('Delete a1.JPG, moving'));
      await userEvent.click(within(question()).getByRole('button', { name: 'Delete 1 file' }));
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
      expect(cards()).toHaveLength(1);

      const undo = notify.mock.calls[notify.mock.calls.length - 1][2] as { run(): void };
      await act(async () => undo.run());
      expect(cards()).toHaveLength(2);
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('does not delete while the folder is still being read', async () => {
    listings[ROOT] = {};
    soFar.mockImplementation((): OrganizeReading => ({ reading: true, total: 7, done: 3, listing: { root: ROOT, fromScan: false, places: PLACES, files: [FILES[1], FILES[5], FILES[3]] } }));
    open();
    await userEvent.click(tile('n1.JPG'));
    await userEvent.click(bin(1));
    expect(said()).toEqual([['Still reading Phone Pictures. Delete files once every file is in.', 'info']]);
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });
});

describe('moving, copying and undoing', () => {
  const butte = (request: OrganizeApplyRequest) => applied({
    done: request.files.length, bytes: 12_186_000,
    items: request.files.map(f => ({ id: f.id, path: `${ROOT}\\Uptown Butte\\${f.id === 'b1' ? 2023 : 2022}\\${f.name}` })),
  });
  /** The listing as it is once the two Butte files have been moved into their folder. */
  const afterMove = () => listing(ROOT, FILES.map(f => (f.place === 1 ? { ...f, id: `${f.id}-moved`, folder: `${ROOT}\\Uptown Butte\\${f.date.slice(0, 4)}` } : f)));

  async function previewOf(name: string) {
    await userEvent.click(within(panel()).getByRole('button', { name: `Preview ${name}` }));
    await waitFor(() => expect(moveButton()).toBeEnabled());
  }

  it('moves a suggested folder\'s files after the preview, says what it did, and marks them as organized', async () => {
    api.applyOrganize.mockImplementation(async (request: OrganizeApplyRequest) => butte(request));
    const { refresh } = open();
    await userEvent.click(tile('b1.JPG'));
    await previewOf('Uptown Butte');
    expect(preview()).toHaveTextContent('Suggested folder');
    expect(within(preview()).getByLabelText('Destination')).toHaveTextContent(`${ROOT}\\Uptown Butte\\`);
    await userEvent.click(moveButton());

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(api.applyOrganize).toHaveBeenCalledExactlyOnceWith({
      basePath: ROOT, folderName: 'Uptown Butte', direct: false, mode: 'move', companions: true, label: 'Uptown Butte',
      files: [{ id: 'b1', name: 'b1.JPG', subfolder: '2023' }, { id: 'b2', name: 'b2.JPG', subfolder: '2022' }],
    });
    expect(notify).toHaveBeenLastCalledWith('Moved 2 files (11.6 MB) to Uptown Butte', 'ok', { label: 'Undo', run: expect.any(Function) });
    // What was selected of them is let go, and once the listing is read again they are tagged where they now are.
    expect(screen.queryByRole('region', { name: 'Selection' })).not.toBeInTheDocument();
    listings[ROOT] = { data: afterMove() };
    refresh();
    expect(tile('b1.JPG')).toHaveTextContent('In Uptown Butte');
    expect(cards()).toEqual(['Anaconda3 files · 17.4 MBJul 2022Inside: a folder for each yearNo landmark known here, so it is named after the town']);
    expect(within(screen.getByRole('group', { name: 'Show' })).getByRole('button', { name: /^Not organized/ })).toHaveTextContent('Not organized5');
    await userEvent.click(within(screen.getByRole('group', { name: 'Show' })).getByRole('button', { name: /^Not organized/ }));
    expect(shownNames()).toEqual(['a1.JPG', 'a2.JPG', 'a3.JPG', 'n1.JPG', 'n2.JPG']);
  });

  it('takes a move back from its message, putting back what had been arranged for the files', async () => {
    api.applyOrganize.mockImplementation(async (request: OrganizeApplyRequest) => butte(request));
    api.undoOrganize.mockResolvedValue({ restored: 3, skipped: 0, problems: [] });
    const { refresh } = open();
    // One of the two had been put in a folder of the person's own, then dropped on the suggestion with the other.
    fireEvent.dragStart(tile('n1.JPG'), dragged());
    fireEvent.drop(within(panel()).getByRole('button', { name: 'Show the files for Uptown Butte' }).parentElement!);
    await previewOf('Uptown Butte');
    api.applyOrganize.mockImplementation(async (request: OrganizeApplyRequest) => applied({ done: 3, bytes: 18_279_000, items: request.files.map(f => ({ id: f.id, path: `${ROOT}\\Uptown Butte\\2022\\${f.name}` })) }));
    await userEvent.click(moveButton());
    await waitFor(() => expect(notify).toHaveBeenLastCalledWith('Moved 3 files (17.4 MB) to Uptown Butte', 'ok', expect.anything()));

    const undo = notify.mock.calls[notify.mock.calls.length - 1][2] as { run(): void };
    await act(async () => undo.run());
    expect(api.undoOrganize).toHaveBeenCalledExactlyOnceWith('b1');
    expect(notify).toHaveBeenLastCalledWith('Moved 3 files back to where they were', 'ok');
    refresh();
    expect(tile('b1.JPG')).not.toHaveTextContent('In Uptown Butte');
    expect(tile('n1.JPG')).toHaveTextContent('For Uptown Butte');
    expect(within(panel()).getByRole('button', { name: 'Show the files for Uptown Butte' })).toHaveTextContent(/^Uptown Butte3 files/);
  });

  it('copies one of your folders to where you choose, and undoes that from the list of what was moved', async () => {
    api.applyOrganize.mockImplementation(async (request: OrganizeApplyRequest) => applied({
      batchId: 'b7', done: 1, bytes: 6_093_000, companions: 2, renamed: 1, items: [{ id: 'a1', path: `${ROOT}\\Trips\\a1 (1).JPG` }],
    }));
    api.undoOrganize.mockResolvedValue({ restored: 1, skipped: 0, problems: [] });
    const { refresh } = open();
    await userEvent.click(tile('a1.JPG'));
    await userEvent.click(within(bar()).getByRole('button', { name: 'New folder from these' }));
    await userEvent.type(within(bar()).getByRole('textbox'), 'Trips{Enter}');
    await userEvent.click(within(panel()).getByRole('button', { name: 'New folder' }));
    await userEvent.type(within(panel()).getByRole('textbox', { name: 'Folder name' }), 'Family{Enter}');
    await previewOf('Trips');
    expect(preview()).toHaveTextContent('Your folder');
    // Renaming it here renames the folder itself.
    await userEvent.type(within(preview()).getByRole('textbox'), ' 2022');
    await userEvent.click(within(screen.getByRole('group', { name: 'Move or copy' })).getByRole('button', { name: 'Copy' }));
    await waitFor(() => expect(moveButton()).toBeEnabled());
    await userEvent.click(moveButton());

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(api.applyOrganize.mock.calls[0][0]).toMatchObject({ folderName: 'Trips 2022', mode: 'copy', label: 'Trips 2022', files: [{ id: 'a1', name: 'a1.JPG' }] });
    expect(notify).toHaveBeenLastCalledWith('Copied 1 file (5.8 MB) to Trips 2022, with 2 Live Photo and edit files. 1 renamed so nothing was replaced', 'ok', expect.anything());
    // A copy leaves the file where it was: it is tagged there, and no longer waits in the folder it was put in.
    expect(tile('a1.JPG')).toHaveTextContent('Copied to Trips 2022');
    expect(within(panel()).getByRole('button', { name: 'Show the files for Trips 2022' })).toHaveTextContent('Empty.');
    expect(within(panel()).getByRole('button', { name: 'Show the files for Family' })).toBeInTheDocument();

    batches = [{ id: 'b7', label: 'Trips 2022', mode: 'copy', count: 1, bytes: 6_093_000, utc: '2026-10-08T18:20:35Z' }];
    refresh();
    await userEvent.click(within(panel()).getByRole('button', { name: 'Undo Trips 2022' }));
    await waitFor(() => expect(notify).toHaveBeenLastCalledWith('Removed the 1 copy from Trips 2022', 'ok'));
    expect(tile('a1.JPG')).toHaveTextContent('For Trips 2022');
  });

  it('undoes a move made before the page was opened, going by what the service says of it', async () => {
    batches = [{ id: 'old-1', label: 'Anaconda', mode: 'copy', count: 9, bytes: 1, utc: '2026-10-08T18:20:35Z' }, { id: 'old-2', label: '2023', mode: 'move', count: 1, bytes: 1, utc: '2026-10-08T18:20:35Z' }];
    api.undoOrganize.mockResolvedValueOnce({ restored: 9, skipped: 0, problems: [] }).mockResolvedValueOnce({ restored: 1, skipped: 2, problems: ['IMG_1.JPG is no longer where it was put.'] })
      .mockResolvedValueOnce({ restored: 0, skipped: 1, problems: [] }).mockRejectedValueOnce(new Error('There is nothing to undo: this was undone already, or was never done.'));
    open();
    await userEvent.click(within(panel()).getByRole('button', { name: 'Undo Anaconda' }));
    await waitFor(() => expect(said()).toEqual([['Removed the 9 copies from Anaconda', 'ok']]));
    await userEvent.click(within(panel()).getByRole('button', { name: 'Undo 2023' }));
    await waitFor(() => expect(said().slice(1)).toEqual([['Moved 1 file back to where it was', 'ok'], ['2 could not be put back. IMG_1.JPG is no longer where it was put.', 'error']]));
    // Nothing put back and no reason given; then a refusal.
    await userEvent.click(within(panel()).getByRole('button', { name: 'Undo 2023' }));
    await waitFor(() => expect(said()[3]).toEqual(['1 could not be put back.', 'error']));
    await userEvent.click(within(panel()).getByRole('button', { name: 'Undo 2023' }));
    await waitFor(() => expect(said()[4]).toEqual(['There is nothing to undo: this was undone already, or was never done.', 'error']));
  });

  it('lists nothing as moved until the service has said what was', () => {
    api.useOrganizeBatches.mockImplementation(() => ({}));
    open();
    expect(within(panel()).queryByRole('heading', { name: 'Recently moved' })).not.toBeInTheDocument();
  });

  it('says what was left where it was, and offers no undo when nothing was moved', async () => {
    api.applyOrganize.mockResolvedValueOnce(applied({ batchId: undefined as unknown as string, done: 0, skipped: 2, problems: ['b1.JPG: Access to the path is denied.'] }))
      .mockResolvedValueOnce(applied({ batchId: undefined as unknown as string, done: 1, bytes: 6_093_000, skipped: 1, items: [{ id: 'b1', path: `${ROOT}\\Uptown Butte\\2023\\b1.JPG` }] }));
    open();
    await previewOf('Uptown Butte');
    await userEvent.click(moveButton());
    await waitFor(() => expect(said()).toEqual([['2 left where they are. b1.JPG: Access to the path is denied.', 'error']]));
    await previewOf('Uptown Butte');
    await userEvent.click(moveButton());
    await waitFor(() => expect(said().slice(1)).toEqual([['Moved 1 file (5.8 MB) to Uptown Butte', 'ok'], ['1 left where it is.', 'error']]));
    expect(notify.mock.calls[1][2]).toBeUndefined();
  });

  it('keeps the preview open, and says why, when the move fails', async () => {
    const waiting = deferred<OrganizeApplyDto>();
    api.applyOrganize.mockReturnValueOnce(waiting.promise);
    open();
    await previewOf('Anaconda');
    await userEvent.click(moveButton());
    expect(moveButton()).toHaveTextContent('Moving…');
    await act(async () => waiting.reject(new TypeError('Failed to fetch')));
    expect(said()).toEqual([['Cannot reach the PhotoSense server.', 'error']]);
    expect(moveButton()).toBeEnabled();
    api.applyOrganize.mockRejectedValueOnce('refused');
    await userEvent.click(moveButton());
    await waitFor(() => expect(said()[1]).toEqual(['refused', 'error']));
    await userEvent.click(within(preview()).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('renames a suggested folder from its preview, and names where files were taken for the name-clash window', async () => {
    api.planOrganize.mockImplementation(async (destination: OrganizeDestination, ids: OrganizePlanFile[]): Promise<OrganizePlanDto> => {
      const folder = `${destination.basePath}\\${destination.folderName}`;
      return {
        destination: folder, exists: true, items: ids.map(f => ({ id: f.id, folder })), companions: 0, taken: { [folder]: ['a1.JPG', 'a2.JPG'] },
        clashes: [
          { id: 'a1', nextFree: 'a1 (1).JPG', existing: [{ id: 'b2', name: 'a1.JPG', sizeBytes: 1, date: '2022-07-04T10:00:00', width: 1, height: 1, isVideo: false, placeName: null, identical: false }] },
          { id: 'a2', nextFree: 'a2 (1).JPG', existing: [{ id: 'n1', name: 'a2.JPG', sizeBytes: 1, date: '2022-07-04T10:00:00', width: 1, height: 1, isVideo: false, placeName: 'Near Butte, Montana, US', identical: false }] },
        ],
      };
    });
    open();
    await previewOf('Anaconda');
    await userEvent.type(within(preview()).getByRole('textbox'), ' home');
    await waitFor(() => expect(moveButton()).toBeEnabled());
    expect(within(preview()).getByLabelText('Destination')).toHaveTextContent(`${ROOT}\\Anaconda home\\`);
    expect(cards()[0]).toMatch(/^Anaconda home3 files · 17\.4 MBJul 2022Inside: a folder for each year$/);

    await userEvent.click(within(preview()).getByRole('button', { name: 'Review each name' }));
    const clash = () => screen.getByRole('dialog', { name: /^Name already taken/ });
    // Both are among the folder's own files: the place of each is given the same way.
    expect(within(clash()).getAllByRole('row')[4]).toHaveTextContent('PlaceAnaconda, MontanaButte, Montana');
    await userEvent.click(within(clash()).getByRole('button', { name: 'Next' }));
    // The file there is one of the folder's own with no place of its own: what the service says of it is used.
    expect(within(clash()).getAllByRole('row')[4]).toHaveTextContent('PlaceAnaconda, MontanaNear Butte, Montana, US');
  });

  it('closes a preview whose folder has nothing left to take', async () => {
    const { refresh } = open();
    await previewOf('Uptown Butte');
    listings[ROOT] = { data: listing(ROOT, FILES.filter(f => f.place !== 1)) };
    refresh();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
