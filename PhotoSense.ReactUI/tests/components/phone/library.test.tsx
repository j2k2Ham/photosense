import { act, fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { LIBRARY_PAGE, LibraryScreen } from '../../../components/phone/Library';
import type { Library } from '../../../lib/library';
import type { PhoneFrame, Platform } from '../../../lib/phone';
import type { OrganizeApplyDto, OrganizeFileDto } from '../../../types';
import { organizeFile, organizePlace } from '../../fixtures';
import { frame, inFrame } from './frame';

const api = vi.hoisted(() => ({
  usePhotoAccess: vi.fn(), planOrganize: vi.fn(), applyOrganize: vi.fn(), removeOrganize: vi.fn(), undoChange: vi.fn(), chooseMorePhotos: vi.fn(),
}));
vi.mock('../../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/apiClient')>()), ...api }));
// Sending is tested on its own; here it says what it was given.
vi.mock('../../../components/SendWindow', () => ({
  SendWindow: ({ files, onClose }: { files: string[]; onClose(): void }) => <div role="dialog" aria-label="Send to computer">Sending {files.join(', ')}<button type="button" onClick={onClose}>Close sending</button></div>,
}));

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

beforeEach(() => {
  for (const mock of Object.values(api)) mock.mockReset();
  api.usePhotoAccess.mockReturnValue({ data: { access: 'everything' } });
  api.planOrganize.mockImplementation(async (destination, asked: { id: string }[]) => ({
    destination: destination.basePath, exists: true, items: asked.map(a => ({ id: a.id, folder: destination.basePath })), clashes: [], taken: { [destination.basePath]: ['IMG_9154.HEIC'] }, companions: 0,
  }));
  api.applyOrganize.mockResolvedValue(applied());
  api.removeOrganize.mockResolvedValue(applied());
  api.undoChange.mockResolvedValue({ restored: 2, skipped: 0, problems: [] });
});

interface ShowProps { lib?: Library; platform?: Platform; more?: Partial<PhoneFrame>; selecting?: boolean }

/** Library in a phone's frame, holding whether it is choosing as PhoneApp does. */
function show({ lib = library(), platform = 'ios', more = {}, selecting: start = false }: ShowProps = {}) {
  const notify = vi.fn(), onSelecting = vi.fn();
  function Holder() {
    const [selecting, setSelecting] = React.useState(start);
    return <LibraryScreen library={lib} selecting={selecting} onSelecting={on => { onSelecting(on); setSelecting(on); }} notify={notify} />;
  }
  const view = inFrame(<Holder />, frame(platform, more));
  return { ...view, notify, onSelecting };
}

const tile = (name: string) => screen.getByRole('button', { name });
const sheet = (name: string | RegExp) => screen.getByRole('dialog', { name });
const toastAction = (notify: ReturnType<typeof vi.fn>) => (notify.mock.calls.at(-1)![2] as { run(): void }).run;

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
    await act(async () => { history.back(); await new Promise(r => setTimeout(r, 20)); });
    expect(screen.getByRole('button', { name: 'Select' })).toBeInTheDocument();
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

  it('sends what is chosen to a computer', async () => {
    show({ selecting: true });
    await userEvent.click(tile('IMG_9154.HEIC'));
    await userEvent.click(within(screen.getByRole('toolbar')).getByRole('button', { name: 'Send' }));
    expect(sheet('Send to computer')).toHaveTextContent('Sending f1');
    expect(screen.queryByRole('toolbar')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Close sending' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

describe('the viewer', () => {
  it('shows a picture whole, when and where it was taken, where it is and its size, and Back closes it', async () => {
    show();
    await userEvent.click(tile('IMG_9154.HEIC'));
    const viewer = sheet('IMG_9154.HEIC');
    expect(within(viewer).getByRole('img', { name: 'IMG_9154.HEIC' })).toHaveAttribute('src', 'http://localhost:7071/api/organize/files/f1/image');
    expect(viewer).toHaveTextContent('Dec 27, 2023, 11:34 PM');
    expect(viewer).toHaveTextContent('West Glacier, Anaconda, Montana, US');
    expect(viewer).toHaveTextContent('Photo Library');
    expect(viewer).toHaveTextContent('5.8 MB · 4032 × 3024');
    await userEvent.click(within(viewer).getByRole('button', { name: 'Back' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await userEvent.click(tile('IMG_9043.HEIC'));
    expect(sheet('IMG_9043.HEIC')).toHaveTextContent('No location');
    expect(sheet('IMG_9043.HEIC')).toHaveTextContent('Photo Library › Anaconda');
    await userEvent.keyboard('a');
    expect(sheet('IMG_9043.HEIC')).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('says a video is not played, with how long it is, and acts on the one file shown', async () => {
    const { notify } = show();
    await userEvent.click(tile('IMG_9080.MOV'));
    const viewer = sheet('IMG_9080.MOV');
    expect(viewer).toHaveTextContent('PhotoSense cannot play videos yet.');
    expect(viewer).toHaveTextContent('5.8 MB · 0:47');
    await userEvent.click(within(viewer).getByRole('button', { name: 'Delete' }));
    await userEvent.click(within(sheet('Delete 1 item?')).getByRole('button', { name: 'Delete 1 item' }));
    expect(api.removeOrganize).toHaveBeenCalledWith(ROOT, ['f2']);
    expect(notify).toHaveBeenLastCalledWith('2 items moved to PhotoSense Removed.', 'ok', expect.anything());
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await userEvent.click(tile('IMG_9154.HEIC'));
    await userEvent.click(within(sheet('IMG_9154.HEIC')).getByRole('button', { name: 'Add to album' }));
    await userEvent.click(within(sheet('Add 1 to an album')).getByRole('button', { name: 'Done' }));
    await userEvent.click(within(sheet('IMG_9154.HEIC')).getByRole('button', { name: 'Send' }));
    expect(sheet('Send to computer')).toHaveTextContent('Sending f1');
  });
});
