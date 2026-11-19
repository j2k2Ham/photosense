import { act, fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { LIBRARY_PAGE, LibraryScreen } from '../../../components/phone/Library';
import type { Album, Library, LibraryFilter } from '../../../lib/library';
import type { PhoneFrame, Platform } from '../../../lib/phone';
import type { LibraryBackupDto, OrganizeApplyDto, OrganizeFileDto, PhotoDto, SafeReviewDto } from '../../../types';
import { organizeFile, organizePlace, plan, safeReview } from '../../fixtures';
import { frame, gate, inFrame } from './frame';

const api = vi.hoisted(() => ({
  usePhotoAccess: vi.fn(), planOrganize: vi.fn(), applyOrganize: vi.fn(), removeOrganize: vi.fn(), undoChange: vi.fn(), chooseMorePhotos: vi.fn(),
  usePhotoDetails: vi.fn(), setFavorite: vi.fn(), sharePhoto: vi.fn(), useSearch: vi.fn(), refreshSearch: vi.fn(),
  useTrips: vi.fn(), useTrip: vi.fn(), useTowns: vi.fn(), useFixes: vi.fn(), useVault: vi.fn(),
}));
vi.mock('../../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/apiClient')>()), ...api }));

const ROOT = '/Photo Library';
const applied = (more: Partial<OrganizeApplyDto> = {}): OrganizeApplyDto => ({ batchId: 'b1', done: 2, bytes: 0, companions: 0, renamed: 0, skipped: 0, problems: [], items: [], ...more });
const files = [
  organizeFile({ id: 'f1', name: 'IMG_9154.HEIC', folder: ROOT, date: '2023-12-27T23:34:00', place: 0 }),
  organizeFile({ id: 'f2', name: 'IMG_9080.MOV', folder: ROOT, date: '2023-12-20T10:00:00', isVideo: true, durationSeconds: 47, width: 0, height: 0 }),
  organizeFile({ id: 'f3', name: 'IMG_9043.HEIC', folder: `${ROOT}/Anaconda`, date: '2023-11-02T08:00:00' }),
];
const library = (more: Partial<Library> = {}): Library => ({
  root: ROOT, files, listing: { root: ROOT, fromScan: true, places: [organizePlace({ area: 'West Glacier' })], files }, ...more,
});

// The plan's gate: no plan known, every removal going ahead, unless a test gives its own.
let planGate = gate();
beforeEach(() => { planGate = gate(); });
beforeEach(() => {
  for (const mock of Object.values(api)) mock.mockReset();
  api.usePhotoAccess.mockReturnValue({ data: { access: 'everything' } });
  api.planOrganize.mockImplementation(async (destination, asked: { id: string }[]) => ({
    destination: destination.basePath, exists: true, items: asked.map(a => ({ id: a.id, folder: destination.basePath })), clashes: [], taken: { [destination.basePath]: ['IMG_9154.HEIC'] }, companions: 0,
  }));
  api.applyOrganize.mockResolvedValue(applied());
  api.removeOrganize.mockResolvedValue(applied());
  api.undoChange.mockResolvedValue({ restored: 2, skipped: 0, problems: [] });
  api.usePhotoDetails.mockReturnValue({ data: { camera: 'iPhone 15 Pro', favorite: false } });
  api.setFavorite.mockResolvedValue({ camera: 'iPhone 15 Pro', favorite: true });
  api.sharePhoto.mockResolvedValue(undefined);
  api.useSearch.mockReturnValue({ data: undefined, error: undefined });
  api.useTrips.mockReturnValue({ data: undefined });
  api.useFixes.mockReturnValue({ data: undefined });
  api.useVault.mockReturnValue({ data: undefined });
});

interface ShowProps { lib?: Library; platform?: Platform; more?: Partial<PhoneFrame>; selecting?: boolean; made?: Album[]; backup?: LibraryBackupDto; safe?: SafeReviewDto; onReview?: () => void; removeLocation?: boolean }

/** Library in a phone's frame, holding whether it is choosing as PhoneApp does. */
function show({ lib = library(), platform = 'ios', more = {}, selecting: start = false, made, backup, safe, onReview, removeLocation }: ShowProps = {}) {
  const notify = vi.fn(), onSelecting = vi.fn(), onSend = vi.fn();
  function Holder() {
    const [selecting, setSelecting] = React.useState(start);
    const [filter, setFilter] = React.useState<LibraryFilter>('all');
    return <LibraryScreen library={lib} selecting={selecting} onSelecting={on => { onSelecting(on); setSelecting(on); }} filter={filter} onFilter={setFilter} made={made} notify={notify} onSend={onSend} backup={backup} safe={safe} onReview={onReview} removeLocation={removeLocation} />;
  }
  const view = inFrame(<Holder />, frame(platform, more), planGate);
  return { ...view, notify, onSelecting, onSend };
}

const tile = (name: string) => screen.getByRole('button', { name });
const sheet = (name: string | RegExp) => screen.getByRole('dialog', { name });
const toastAction = (notify: ReturnType<typeof vi.fn>) => (notify.mock.calls.at(-1)![2] as { run(): void }).run;

describe("Library's Year in review card", () => {
  it('shows while the year is due, and not while choosing; Watch opens it', async () => {
    const onYear = vi.fn();
    const year = { year: 2026, photos: 7512, videos: 316, trips: 3, topTrips: [], months: Array(12).fill(0), busiestMonth: 0, busiestPlace: null, freed: 41_600_000_000, removed: 0, longestStreak: 0, sessions: 0 };
    const ui = (selecting: boolean, review = year) => (
      <LibraryScreen library={library()} selecting={selecting} onSelecting={vi.fn()} filter="all" onFilter={vi.fn()} notify={vi.fn()} onSend={vi.fn()} year={review} onYear={onYear} />
    );
    const { again } = inFrame(ui(false));
    await userEvent.click(within(screen.getByRole('region', { name: 'Year in review' })).getByRole('button', { name: 'Watch' }));
    expect(onYear).toHaveBeenCalledOnce();
    again(ui(true));
    expect(screen.queryByRole('region', { name: 'Year in review' })).not.toBeInTheDocument();
    again(ui(false, { ...year, year: null as unknown as number }));
    expect(screen.queryByRole('region', { name: 'Year in review' })).not.toBeInTheDocument();
  });
});

describe('Library', () => {
  it('shows every picture and video, latest first, by month, with each filter\'s count', async () => {
    show();
    expect(screen.getByRole('heading', { level: 1, name: 'Library' })).toBeInTheDocument();
    expect(screen.getByText('3 photos and videos')).toBeInTheDocument();
    const filters = within(screen.getByRole('group', { name: 'Show' })).getAllByRole('button');
    expect(filters.map(b => b.textContent)).toEqual(['All 3', 'Not organized 2', 'No location 2', 'Videos 1']);
    expect(screen.getAllByRole('region').map(r => r.getAttribute('aria-label'))).toEqual(['Library', 'December 2023', 'November 2023']);
    expect(within(screen.getByRole('region', { name: 'December 2023' })).getByText('2 items')).toBeInTheDocument();
    // A video has no preview: a play mark, and how long it plays.
    expect(tile('IMG_9080.MOV').querySelector('img')).toBeNull();
    expect(tile('IMG_9080.MOV')).toHaveTextContent('0:47');
    expect(tile('IMG_9154.HEIC').querySelector('img')).toHaveAttribute('src', 'http://localhost:7071/api/organize/files/f1/thumbnail');

    await userEvent.click(filters[3]);
    expect(screen.getAllByRole('button', { name: /^IMG_/ }).map(b => b.getAttribute('aria-label'))).toEqual(['IMG_9080.MOV']);
    await userEvent.click(filters[1]);
    expect(screen.getAllByRole('button', { name: /^IMG_/ })).toHaveLength(2);
  });

  it('says when a filter or the library has nothing, and when the library could not be read', () => {
    const view = show({ lib: library({ files: [], listing: undefined, root: undefined }) });
    expect(screen.getByText('No photos or videos yet.')).toBeInTheDocument();
    expect(screen.getByText('0 photos and videos')).toBeInTheDocument();
    view.unmount();
    const one = [files[0]];
    show({ lib: library({ files: one, error: new Error('The library is locked.') }) });
    expect(screen.getByRole('alert')).toHaveTextContent('PhotoSense could not read your photos: The library is locked.');
    expect(screen.getByText('1 photo or video')).toBeInTheDocument();
  });

  it('says when a filter shows nothing', async () => {
    show({ lib: library({ files: [files[0]] }) });
    await userEvent.click(screen.getByRole('button', { name: 'Videos 0' }));
    expect(screen.getByText('Nothing here with this filter.')).toBeInTheDocument();
  });

  it('says how far it has read, and offers no Select until it is through', () => {
    const view = show({ lib: library({ reading: { done: 0, total: 0 } }) });
    expect(screen.getByText('Reading your photos…')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Select' })).not.toBeInTheDocument();
    view.unmount();
    show({ lib: library({ reading: { done: 1200, total: 6941 } }) });
    expect(screen.getByText('Reading… 1,200 of 6,941')).toBeInTheDocument();
  });

  it('with only some photos allowed, says so, and Choose more opens the phone\'s picker', async () => {
    api.usePhotoAccess.mockReturnValue({ data: { access: 'selected' } });
    api.chooseMorePhotos.mockResolvedValueOnce({ access: 'everything' }).mockResolvedValueOnce({ access: 'selected' }).mockRejectedValueOnce(new Error('No picker.'));
    const { notify } = show();
    expect(screen.getByText('The 3 photos you chose')).toBeInTheDocument();
    const note = screen.getByRole('note');
    expect(note).toHaveTextContent('PhotoSense can see only the 3 photos you chose. Copies among the rest are not found.');
    for (let i = 0; i < 3; i++) await userEvent.click(within(note).getByRole('button', { name: 'Choose more' }));
    expect(notify.mock.calls).toEqual([
      ['PhotoSense can now see all your photos.', 'ok'], ['PhotoSense can see the photos you selected.', 'ok'], ['The photo picker could not be shown: No picker.', 'error'],
    ]);
  });

  it('draws more of a long library as its end comes into view', () => {
    let seen: (entries: { isIntersecting: boolean }[]) => void = () => {};
    const disconnect = vi.fn();
    vi.stubGlobal('IntersectionObserver', class { constructor(f: typeof seen) { seen = f; } observe() {} disconnect = disconnect; });
    const many: OrganizeFileDto[] = Array.from({ length: LIBRARY_PAGE + 10 }, (_, i) => organizeFile({ id: `m${i}`, name: `IMG_${i}.JPG`, folder: ROOT, date: '2023-12-01T00:00:00' }));
    show({ lib: library({ files: many }) });
    expect(screen.getAllByRole('button', { name: /^IMG_/ })).toHaveLength(LIBRARY_PAGE);
    act(() => seen([{ isIntersecting: false }]));
    expect(screen.getAllByRole('button', { name: /^IMG_/ })).toHaveLength(LIBRARY_PAGE);
    act(() => seen([{ isIntersecting: true }]));
    expect(screen.getAllByRole('button', { name: /^IMG_/ })).toHaveLength(LIBRARY_PAGE + 10);
    expect(disconnect).toHaveBeenCalled();
  });
});

describe('choosing in Library', () => {
  it('on an iPhone: Select, the count in the title, Cancel, and the selection bar in place of the tabs', async () => {
    const { onSelecting } = show();
    await userEvent.click(screen.getByRole('button', { name: 'Select' }));
    expect(screen.getByRole('heading', { level: 1, name: 'Select items' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Settings' })).not.toBeInTheDocument();
    const bar = screen.getByRole('toolbar', { name: 'What is selected' });
    expect(within(bar).getByRole('button', { name: 'Delete' })).toBeDisabled();
    await userEvent.click(tile('IMG_9154.HEIC'));
    await userEvent.click(tile('IMG_9043.HEIC'));
    await userEvent.click(tile('IMG_9043.HEIC'));
    expect(screen.getByRole('heading', { level: 1, name: '1 selected' })).toBeInTheDocument();
    expect(tile('IMG_9154.HEIC')).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onSelecting.mock.calls).toEqual([[true], [false]]);
    expect(screen.queryByRole('toolbar')).not.toBeInTheDocument();
  });

  it('on Android: a cross before the title stops choosing, and Back does too', async () => {
    show({ platform: 'android', selecting: true });
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Stop selecting' }));
    expect(screen.getByRole('button', { name: 'Select' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Select' }));
    // Back is told by the browser when it gets to it, which takes longer under load: waited for, not timed.
    act(() => history.back());
    expect(await screen.findByRole('button', { name: 'Select' })).toBeInTheDocument();
  });

  it('deletes what is chosen after a question naming it, says where it went, and Undo brings it back', async () => {
    const { notify } = show({ selecting: true });
    await userEvent.click(tile('IMG_9154.HEIC'));
    await userEvent.click(tile('IMG_9080.MOV'));
    await userEvent.click(within(screen.getByRole('toolbar')).getByRole('button', { name: 'Delete' }));
    const question = sheet('Delete 2 items?');
    expect(question).toHaveTextContent('IMG_9154.HEIC, IMG_9080.MOV go to PhotoSense Removed.');
    expect(question).toHaveTextContent('Undo can bring them back. Delete permanently, in Settings, sends them to Recently Deleted.');
    await userEvent.click(within(question).getByRole('button', { name: 'Delete 2 items' }));
    expect(api.removeOrganize).toHaveBeenCalledWith(ROOT, ['f1', 'f2']);
    expect(notify).toHaveBeenLastCalledWith('2 items moved to PhotoSense Removed.', 'ok', expect.objectContaining({ label: 'Undo' }));
    expect(screen.queryByRole('toolbar')).not.toBeInTheDocument();

    await act(async () => toastAction(notify)());
    expect(api.undoChange).toHaveBeenCalledWith('b1');
    expect(notify).toHaveBeenLastCalledWith('Moved 2 items back.', 'ok');
  });

  it('on the free plan asks the plan first, deletes the largest when fewer are left, and says the removals left in the question', async () => {
    planGate = gate({ plan: plan({ leftToday: 2 }), remove: vi.fn((_count: number, _label: string, run: (allowed: number) => void) => run(2)) });
    show({ selecting: true });
    for (const name of ['IMG_9154.HEIC', 'IMG_9080.MOV', 'IMG_9043.HEIC']) await userEvent.click(tile(name));
    await userEvent.click(within(screen.getByRole('toolbar')).getByRole('button', { name: 'Delete' }));
    expect(planGate.remove).toHaveBeenCalledWith(3, 'items', expect.any(Function));
    const question = sheet('Delete 2 items?');
    expect(question).toHaveTextContent('Free plan: 2 removals left today.');
    await userEvent.click(within(question).getByRole('button', { name: 'Delete 2 items' }));
    expect(api.removeOrganize).toHaveBeenCalledWith(ROOT, ['f1', 'f2']);
  });

  it('puts what is chosen in an album, each under a name nothing has, and Undo takes it back', async () => {
    const { notify } = show({ selecting: true });
    await userEvent.click(tile('IMG_9154.HEIC'));
    await userEvent.click(within(screen.getByRole('toolbar')).getByRole('button', { name: 'Add to album' }));
    const put = sheet('Add 1 to an album');
    await userEvent.click(within(put).getByRole('button', { name: 'Anaconda' }));
    expect(api.planOrganize).toHaveBeenCalledWith({ basePath: `${ROOT}/Anaconda`, folderName: '', direct: true }, [{ id: 'f1' }]);
    expect(api.applyOrganize).toHaveBeenCalledWith({
      basePath: `${ROOT}/Anaconda`, folderName: '', direct: true, mode: 'move', companions: true, label: 'Anaconda', files: [{ id: 'f1', name: 'IMG_9154 (1).HEIC' }],
    });
    expect(notify).toHaveBeenLastCalledWith('Moved 2 items to Anaconda.', 'ok', expect.objectContaining({ label: 'Undo' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('offers the albums named in Organize, made with the first files put in them, beside those there are', async () => {
    const made = [{ path: `${ROOT}/Lake`, label: 'Lake', base: ROOT }, { path: `${ROOT}/Anaconda`, label: 'Anaconda', base: ROOT }];
    show({ selecting: true, made });
    await userEvent.click(tile('IMG_9154.HEIC'));
    await userEvent.click(within(screen.getByRole('toolbar')).getByRole('button', { name: 'Add to album' }));
    const put = sheet('Add 1 to an album');
    // An album named and since made is offered once.
    expect(within(put).getAllByRole('button').map(b => b.textContent)).toEqual(['Done', 'Anaconda', 'Lake', '+ New album…']);
    await userEvent.click(within(put).getByRole('button', { name: 'Lake' }));
    expect(api.planOrganize).toHaveBeenCalledWith({ basePath: ROOT, folderName: 'Lake', direct: false }, [{ id: 'f1' }]);
  });

  it('makes a new album or folder for what is chosen, once it has a name no other has', async () => {
    // A move that leaves nothing to undo offers no Undo.
    api.applyOrganize.mockResolvedValueOnce(applied({ batchId: '' }));
    const { notify } = show({ platform: 'android', selecting: true });
    await userEvent.click(tile('IMG_9154.HEIC'));
    await userEvent.click(within(screen.getByRole('toolbar')).getByRole('button', { name: 'Move to folder' }));
    await userEvent.click(within(sheet('Move 1 to a folder')).getByRole('button', { name: '+ New folder…' }));
    const made = sheet('New folder');
    const name = within(made).getByRole('textbox', { name: 'Folder name' });
    expect(within(made).getByRole('button', { name: 'Create' })).toBeDisabled();
    fireEvent.submit(name);
    expect(within(made).getByRole('alert')).toHaveTextContent('Give the new folder a name.');
    await userEvent.type(name, 'Caras Park');
    expect(within(made).queryByRole('alert')).not.toBeInTheDocument();
    await userEvent.click(within(made).getByRole('button', { name: 'Create' }));
    expect(api.applyOrganize).toHaveBeenCalledWith(expect.objectContaining({ basePath: '/Photo Library/Pictures', folderName: 'Caras Park', direct: false, label: 'Caras Park' }));
    expect(notify).toHaveBeenLastCalledWith('Moved 2 items to Caras Park.', 'ok', undefined);
  });

  it('refuses a new album with the name of one already there, in the iPhone\'s word', async () => {
    show({ selecting: true });
    await userEvent.click(tile('IMG_9154.HEIC'));
    await userEvent.click(within(screen.getByRole('toolbar')).getByRole('button', { name: 'Add to album' }));
    await userEvent.click(within(sheet('Add 1 to an album')).getByRole('button', { name: '+ New album…' }));
    await userEvent.type(within(sheet('New album')).getByRole('textbox', { name: 'Album name' }), 'anaconda{Enter}');
    expect(within(sheet('New album')).getByRole('alert')).toHaveTextContent('You already have an album called anaconda.');
    expect(api.applyOrganize).not.toHaveBeenCalled();
    await userEvent.click(within(sheet('New album')).getByRole('button', { name: 'Done' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('says what could not be moved, what went wrong, and when nothing was done or could be undone', async () => {
    api.applyOrganize.mockResolvedValueOnce(applied({ done: 0, batchId: '', skipped: 1, problems: ['The album is gone.'] }));
    api.removeOrganize.mockRejectedValueOnce(new Error('Cannot reach the phone.'));
    api.undoChange.mockResolvedValueOnce({ restored: 0, skipped: 2, problems: ['Moved since.'] }).mockResolvedValueOnce({ restored: 0, skipped: 1, problems: [] }).mockRejectedValueOnce('no undo');
    const { notify } = show({ selecting: true });
    const choose = async () => { await userEvent.click(tile('IMG_9154.HEIC')); };

    await choose();
    await userEvent.click(within(screen.getByRole('toolbar')).getByRole('button', { name: 'Add to album' }));
    await userEvent.click(within(sheet('Add 1 to an album')).getByRole('button', { name: 'Anaconda' }));
    expect(notify.mock.calls).toEqual([['1 left where it is. The album is gone.', 'error']]);

    await userEvent.click(screen.getByRole('button', { name: 'Select' }));
    await choose();
    await userEvent.click(within(screen.getByRole('toolbar')).getByRole('button', { name: 'Delete' }));
    await userEvent.click(within(sheet('Delete 1 item?')).getByRole('button', { name: 'Delete 1 item' }));
    expect(notify).toHaveBeenLastCalledWith('Cannot reach the phone.', 'error');
    await userEvent.click(within(sheet('Delete 1 item?')).getByRole('button', { name: 'Cancel' }));

    api.removeOrganize.mockResolvedValueOnce(applied({ skipped: 2, problems: [] }));
    await userEvent.click(within(screen.getByRole('toolbar')).getByRole('button', { name: 'Delete' }));
    await userEvent.click(within(sheet('Delete 1 item?')).getByRole('button', { name: 'Delete 1 item' }));
    expect(notify).toHaveBeenCalledWith('2 left where they are.', 'error');
    const undo = (notify.mock.calls.find(c => c[0] === '2 items moved to PhotoSense Removed.')![2] as { run(): void }).run;
    await act(async () => undo());
    expect(notify).toHaveBeenLastCalledWith('2 could not be put back. Moved since.', 'error');
    await act(async () => undo());
    expect(notify).toHaveBeenLastCalledWith('1 could not be put back.', 'error');
    await act(async () => undo());
    expect(notify).toHaveBeenLastCalledWith('no undo', 'error');
  });

  it('takes what is chosen to Send, and stops choosing', async () => {
    const { onSend } = show({ selecting: true });
    await userEvent.click(tile('IMG_9154.HEIC'));
    await userEvent.click(within(screen.getByRole('toolbar')).getByRole('button', { name: 'Send' }));
    expect(onSend).toHaveBeenCalledExactlyOnceWith(['f1']);
    expect(screen.queryByRole('toolbar')).not.toBeInTheDocument();
  });
});

describe('the viewer', () => {
  it('shows a picture whole under its date, place and time, and Info says what it is and where; Back and Escape close it', async () => {
    show();
    await userEvent.click(tile('IMG_9154.HEIC'));
    const viewer = sheet('IMG_9154.HEIC');
    expect(within(viewer).getByRole('img', { name: 'IMG_9154.HEIC' })).toHaveAttribute('src', 'http://localhost:7071/api/organize/files/f1/image');
    expect(viewer).toHaveTextContent('Dec 27, 2023West Glacier, Anaconda, Montana, US · 11:34 PM');
    expect(api.usePhotoDetails).toHaveBeenLastCalledWith('/Photo Library/IMG_9154.HEIC');
    const tools = within(viewer).getByRole('toolbar', { name: 'Photo' });
    expect(within(tools).getAllByRole('button').map(b => b.getAttribute('aria-label'))).toEqual(['Share', 'Favorite', 'Info', 'Delete']);
    expect(within(viewer).queryByRole('region', { name: 'Info' })).not.toBeInTheDocument();
    await userEvent.click(within(tools).getByRole('button', { name: 'Info' }));
    expect(within(tools).getByRole('button', { name: 'Info' })).toHaveAttribute('aria-pressed', 'true');
    const info = within(viewer).getByRole('region', { name: 'Info' });
    expect(info).toHaveTextContent('NameIMG_9154.HEICDateDec 27, 2023, 11:34 PMPlaceWest Glacier, Anaconda, Montana, USCameraiPhone 15 ProSize5.8 MB · 4032 × 3024AlbumPhoto Library');
    await userEvent.click(within(tools).getByRole('button', { name: 'Info' }));
    expect(within(viewer).queryByRole('region', { name: 'Info' })).not.toBeInTheDocument();
    await userEvent.click(within(viewer).getByRole('button', { name: 'Back' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    // A camera not yet said, and one the picture does not say.
    api.usePhotoDetails.mockReturnValue({ data: undefined });
    await userEvent.click(tile('IMG_9043.HEIC'));
    expect(sheet('IMG_9043.HEIC')).toHaveTextContent('Nov 2, 2023No location · 8:00 AM');
    await userEvent.click(within(sheet('IMG_9043.HEIC')).getByRole('button', { name: 'Info' }));
    expect(within(sheet('IMG_9043.HEIC')).getByRole('region', { name: 'Info' })).toHaveTextContent('PlaceNo locationCamera…');
    expect(within(sheet('IMG_9043.HEIC')).getByRole('region', { name: 'Info' })).toHaveTextContent('AlbumPhoto Library › Anaconda');
    api.usePhotoDetails.mockReturnValue({ data: { camera: null, favorite: false } });
    await userEvent.click(within(sheet('IMG_9043.HEIC')).getByRole('button', { name: 'Info' }));
    await userEvent.click(within(sheet('IMG_9043.HEIC')).getByRole('button', { name: 'Info' }));
    expect(within(sheet('IMG_9043.HEIC')).getByRole('region', { name: 'Info' })).toHaveTextContent('CameraNot known');
    // A key the viewer does not use leaves it open.
    await userEvent.keyboard('a');
    expect(sheet('IMG_9043.HEIC')).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('on Android, a bar with labels; a video is not played; Info sends or moves the one file shown, and Delete asks first', async () => {
    const { notify, onSend } = show({ platform: 'android' });
    await userEvent.click(tile('IMG_9080.MOV'));
    const viewer = sheet('IMG_9080.MOV');
    expect(viewer).toHaveTextContent('PhotoSense cannot play videos yet.');
    expect(within(viewer).getByRole('toolbar', { name: 'Photo' })).toHaveTextContent('ShareFavoriteInfoDelete');
    await userEvent.click(within(viewer).getByRole('button', { name: 'Info' }));
    expect(within(viewer).getByRole('region', { name: 'Info' })).toHaveTextContent('Size5.8 MB · 0:47FolderPhone');
    await userEvent.click(within(viewer).getByRole('button', { name: 'Delete' }));
    await userEvent.click(within(sheet('Delete 1 item?')).getByRole('button', { name: 'Delete 1 item' }));
    expect(api.removeOrganize).toHaveBeenCalledWith(ROOT, ['f2']);
    expect(notify).toHaveBeenLastCalledWith('2 items moved to Pictures/PhotoSense Removed.', 'ok', expect.anything());
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await userEvent.click(tile('IMG_9154.HEIC'));
    await userEvent.click(within(sheet('IMG_9154.HEIC')).getByRole('button', { name: 'Info' }));
    await userEvent.click(within(sheet('IMG_9154.HEIC')).getByRole('button', { name: 'Move to folder' }));
    await userEvent.click(within(sheet('Move 1 to a folder')).getByRole('button', { name: 'Done' }));
    await userEvent.click(within(sheet('IMG_9154.HEIC')).getByRole('button', { name: 'Send' }));
    expect(onSend).toHaveBeenCalledExactlyOnceWith(['f1']);
    expect(screen.queryByRole('dialog', { name: 'IMG_9154.HEIC' })).not.toBeInTheDocument();
  });

  it('marks a favorite in the phone\'s own photos, shows the mark, and says why when the phone refuses', async () => {
    const { notify } = show();
    await userEvent.click(tile('IMG_9154.HEIC'));
    const favorite = within(sheet('IMG_9154.HEIC')).getByRole('button', { name: 'Favorite' });
    expect(favorite).toHaveAttribute('aria-pressed', 'false');
    await userEvent.click(favorite);
    expect(api.setFavorite).toHaveBeenCalledWith('/Photo Library/IMG_9154.HEIC', true);
    api.usePhotoDetails.mockReturnValue({ data: { camera: 'iPhone 15 Pro', favorite: true } });
    api.setFavorite.mockRejectedValue(new Error('You chose not to let PhotoSense change this photo.'));
    await userEvent.click(within(sheet('IMG_9154.HEIC')).getByRole('button', { name: 'Info' }));
    expect(favorite).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(favorite);
    expect(api.setFavorite).toHaveBeenLastCalledWith('/Photo Library/IMG_9154.HEIC', false);
    expect(notify).toHaveBeenLastCalledWith('You chose not to let PhotoSense change this photo.', 'error');
    api.setFavorite.mockRejectedValue('The phone is busy.');
    await userEvent.click(favorite);
    expect(notify).toHaveBeenLastCalledWith('The phone is busy.', 'error');
  });

  it('shares a copy without the place to start with, or the camera too, as asked; the library keeps both', async () => {
    const { notify } = show();
    await userEvent.click(tile('IMG_9154.HEIC'));
    await userEvent.click(within(sheet('IMG_9154.HEIC')).getByRole('button', { name: 'Share' }));
    const share = sheet('Share photo');
    expect(share).toHaveTextContent('Shares a copy without location');
    expect(share).toHaveTextContent('West Glacier, Anaconda, Montana, US is left out of the copy.');
    expect(share).toHaveTextContent('The photo in your library keeps its location and details. Only the shared copy changes.');
    await userEvent.click(within(share).getByText('Remove camera details'));
    expect(share).toHaveTextContent('Shares a copy without location or camera details');
    await userEvent.click(within(share).getByText('Remove location'));
    expect(share).toHaveTextContent('Shares a copy without camera details');
    expect(share).toHaveTextContent('The copy includes West Glacier, Anaconda, Montana, US.');
    api.sharePhoto.mockRejectedValueOnce(new Error('PhotoSense has to be open on the screen to share.'));
    await userEvent.click(within(share).getByRole('button', { name: 'Share' }));
    expect(api.sharePhoto).toHaveBeenLastCalledWith('/Photo Library/IMG_9154.HEIC', false, true);
    expect(notify).toHaveBeenLastCalledWith('PhotoSense has to be open on the screen to share.', 'error');
    await userEvent.click(within(share).getByRole('button', { name: 'Share' }));
    expect(screen.queryByRole('dialog', { name: 'Share photo' })).not.toBeInTheDocument();
  });

  it('starts with the place kept when Settings says so, says when a file has none, and Not now puts it away', async () => {
    show({ removeLocation: false });
    await userEvent.click(tile('IMG_9080.MOV'));
    await userEvent.click(within(sheet('IMG_9080.MOV')).getByRole('button', { name: 'Share' }));
    const share = sheet('Share photo');
    expect(share).toHaveTextContent('Shares the original file');
    expect(share).toHaveTextContent('This photo does not say where it was taken.');
    expect(share).toHaveTextContent('Video');
    await userEvent.click(within(share).getByRole('button', { name: 'Not now' }));
    expect(screen.queryByRole('dialog', { name: 'Share photo' })).not.toBeInTheDocument();
    expect(sheet('IMG_9080.MOV')).toBeInTheDocument();
  });
});

describe("Library's search", () => {
  it('opens Search from the field, not while choosing, and opens a match in the viewer', async () => {
    api.useSearch.mockImplementation((q: string) => ({ data: q ? { parts: [{ kind: 'place', text: 'West Glacier' }], photos: [{ id: 'p1', fileName: 'IMG_9154.HEIC', sourcePath: '/Photo Library/IMG_9154.HEIC', isVideo: false }], total: 1, videos: 0, proNeeded: false } : undefined }));
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Search photos' }));
    await userEvent.type(within(sheet('Search')).getByRole('searchbox', { name: 'Search photos' }), 'West Glacier{Enter}');
    await userEvent.click(within(sheet('Search')).getByRole('button', { name: 'IMG_9154.HEIC' }));
    expect(sheet('IMG_9154.HEIC')).toBeInTheDocument();
    await userEvent.click(within(sheet('IMG_9154.HEIC')).getByRole('button', { name: 'Back' }));
    await userEvent.click(within(sheet('Search')).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('has no search field while choosing', () => {
    show({ selecting: true });
    expect(screen.queryByRole('button', { name: 'Search photos' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Trips/ })).not.toBeInTheDocument();
    expect(api.useTrips).toHaveBeenLastCalledWith(false);
    expect(api.useFixes).toHaveBeenLastCalledWith(false);
  });

  it('opens Fix dates and places from its tile, and Back closes it', async () => {
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Needs fixing, Looking…' }));
    expect(sheet('Fix dates and places')).toHaveTextContent('Looking at dates and places…');
    await userEvent.click(within(sheet('Fix dates and places')).getByRole('button', { name: 'Back' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('opens the hidden vault from its tile, and Back closes it', async () => {
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Hidden, Looking…' }));
    expect(sheet('Hidden')).toBeInTheDocument();
    await userEvent.click(within(sheet('Hidden')).getByRole('button', { name: 'Back' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it("opens Trips from its tile, a trip's photo in the viewer, which Escape closes first, and Trips after", async () => {
    const cover = { id: 'p1', fileName: 'IMG_9154.HEIC', sourcePath: '/Photo Library/IMG_9154.HEIC', isVideo: false } as PhotoDto;
    const trip = { id: 'gla', name: 'Glacier National Park', folder: '2023-12 Glacier National Park', start: '2023-12-27', end: '2023-12-27', photos: 12, videos: 0, latitude: 48.7, longitude: -113.8, cover, folderMade: false, suggested: 'Glacier National Park' };
    api.useTrips.mockReturnValue({ data: { home: { town: 'Bozeman', region: 'Montana', country: 'US', latitude: 45.7, longitude: -111, days: 0 }, homeChosen: false, trips: [trip], computer: null, tripsFolder: null, folder: null } });
    api.useTrip.mockReturnValue({ data: { trip, days: [{ date: '2023-12-27', photos: [cover] }] } });
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Trips, 1 trip' }));
    await userEvent.click(within(sheet('Trips')).getByRole('button', { name: /^Glacier National Park/ }));
    await userEvent.click(within(sheet('Glacier National Park')).getByRole('button', { name: 'IMG_9154.HEIC' }));
    expect(sheet('IMG_9154.HEIC')).toBeInTheDocument();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.queryByRole('dialog', { name: 'IMG_9154.HEIC' })).not.toBeInTheDocument();
    expect(sheet('Glacier National Park')).toBeInTheDocument();
    fireEvent.keyDown(window, { key: 'Escape' });
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

describe('Library, once the phone backs up to a computer', () => {
  const backup = (more: Partial<LibraryBackupDto> = {}): LibraryBackupDto => ({ computer: 'Studio iMac', onComputer: ['f1', 'f3'], waiting: ['f2'], lastChecked: new Date().toISOString(), ...more });

  it('says how many are on the computer and how much removing them frees, Review opens Safe to delete, and it can be hidden', async () => {
    const onReview = vi.fn();
    show({ backup: backup(), safe: safeReview(), onReview });
    const banner = screen.getByRole('region', { name: 'On Studio iMac' });
    expect(banner).toHaveTextContent('6,561 items are on Studio iMacRemove them from this iPhone to free up to 31.1 GB.');
    await userEvent.click(within(banner).getByRole('button', { name: 'Review' }));
    expect(onReview).toHaveBeenCalledOnce();
    await userEvent.click(within(banner).getByRole('button', { name: 'Hide' }));
    expect(screen.queryByRole('region', { name: 'On Studio iMac' })).not.toBeInTheDocument();
  });

  it('says one is on the computer, on a phone, and has no banner while nothing is or while choosing', () => {
    const { again } = show({ backup: backup(), safe: safeReview({ checked: { count: 1, bytes: 1 } }), platform: 'android' });
    expect(screen.getByRole('region', { name: 'On Studio iMac' })).toHaveTextContent('1 item is on Studio iMacRemove them from this phone');
    again(<LibraryScreen library={library()} selecting={false} onSelecting={vi.fn()} filter="all" onFilter={vi.fn()} notify={vi.fn()} onSend={vi.fn()} backup={backup()} safe={safeReview({ checked: { count: 0, bytes: 0 } })} />);
    expect(screen.queryByRole('region', { name: 'On Studio iMac' })).not.toBeInTheDocument();
    again(<LibraryScreen library={library()} selecting onSelecting={vi.fn()} filter="all" onFilter={vi.fn()} notify={vi.fn()} onSend={vi.fn()} backup={backup()} safe={safeReview()} />);
    expect(screen.queryByRole('region', { name: 'On Studio iMac' })).not.toBeInTheDocument();
  });

  it('ticks what is on the computer, filters by it and by what waits, and says when it was checked', async () => {
    show({ backup: backup() });
    const filters = within(screen.getByRole('group', { name: 'Show' })).getAllByRole('button');
    expect(filters.map(b => b.textContent)).toEqual(['All 3', 'On Studio iMac 2', 'Waiting 1', 'Not organized 2', 'No location 2', 'Videos 1']);
    expect(within(tile('IMG_9154.HEIC')).getByRole('img', { name: 'On the computer' })).toBeInTheDocument();
    expect(within(tile('IMG_9080.MOV')).queryByRole('img', { name: 'On the computer' })).not.toBeInTheDocument();
    expect(screen.getByText(/^On Studio iMac, checked today at /)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Waiting 1' }));
    expect(screen.getAllByRole('button', { name: /^IMG_/ }).map(b => b.getAttribute('aria-label'))).toEqual(['IMG_9080.MOV']);
    await userEvent.click(screen.getByRole('button', { name: 'On Studio iMac 2' }));
    expect(screen.getAllByRole('button', { name: /^IMG_/ }).map(b => b.getAttribute('aria-label'))).toEqual(['IMG_9154.HEIC', 'IMG_9043.HEIC']);
  });

  it('with no computer, or never checked, has neither the filters nor the line', () => {
    const { again } = show({ backup: backup({ computer: null }) });
    expect(within(screen.getByRole('group', { name: 'Show' })).getAllByRole('button')).toHaveLength(4);
    again(<LibraryScreen library={library()} selecting={false} onSelecting={vi.fn()} filter="all" onFilter={vi.fn()} notify={vi.fn()} onSend={vi.fn()} backup={backup({ lastChecked: null })} />);
    expect(screen.queryByText(/checked/)).not.toBeInTheDocument();
  });
});


describe('Library on a tablet (T1)', () => {
  const year = { year: 2026, photos: 7512, videos: 316, trips: 3, topTrips: [], months: Array(12).fill(0), busiestMonth: 0, busiestPlace: null, freed: 41_600_000_000, removed: 0, longestStreak: 0, sessions: 0 };
  const onComputer: LibraryBackupDto = { computer: 'Studio iMac', onComputer: ['f1'], waiting: [], lastChecked: null };

  it('puts the search field beside the title, and what is on the computer, Trips and Year in review in a row; the collections are in the sidebar', async () => {
    const onCollection = vi.fn();
    inFrame(<LibraryScreen library={library()} selecting={false} onSelecting={vi.fn()} filter="all" onFilter={vi.fn()} notify={vi.fn()} onSend={vi.fn()}
      backup={onComputer} safe={safeReview()} year={year} onYear={vi.fn()} onCollection={onCollection} />, frame('ios', { tablet: true, form: 'tablet' }), planGate);
    expect(screen.getByRole('region', { name: 'On Studio iMac' })).toHaveClass('flex-[1.4]');
    expect(screen.getByRole('region', { name: 'On Studio iMac' })).toHaveTextContent('Remove them from this iPad');
    expect(screen.getByRole('region', { name: 'Year in review' })).toHaveClass('flex-col');
    expect(screen.queryByRole('button', { name: /^Needs fixing/ })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Trips, Finding trips…' }));
    expect(onCollection).toHaveBeenCalledWith('trips');
    await userEvent.click(screen.getByRole('button', { name: 'Search photos' }));
    expect(screen.getByRole('dialog', { name: 'Search' })).toBeInTheDocument();
  });

  it('draws the collection open where the shell keeps room for it beside the sidebar, with no way back of its own', () => {
    const slot = document.body.appendChild(document.createElement('div'));
    const ui = (collection?: 'fixes' | 'hidden') => (
      <LibraryScreen library={library()} selecting={false} onSelecting={vi.fn()} filter="all" onFilter={vi.fn()} notify={vi.fn()} onSend={vi.fn()} collection={collection} onCollection={vi.fn()} />
    );
    const { again } = inFrame(ui('fixes'), frame('ios', { tablet: true, form: 'tablet', placeSlot: slot }), planGate);
    expect(within(slot).getByRole('region', { name: 'Fix dates and places' })).toBeInTheDocument();
    expect(within(slot).queryByRole('button', { name: 'Back' })).not.toBeInTheDocument();
    again(ui('hidden'));
    expect(within(slot).getByRole('region', { name: 'Hidden' })).toBeInTheDocument();
    expect(within(slot).queryByRole('button', { name: 'Back' })).not.toBeInTheDocument();
    again(ui());
    expect(slot).toBeEmptyDOMElement();
    slot.remove();
  });
});
