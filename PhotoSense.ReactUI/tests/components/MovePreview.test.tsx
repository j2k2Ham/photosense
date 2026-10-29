import React from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MovePreview } from '../../components/organize/MovePreview';
import { NameClashWindow } from '../../components/organize/NameClashWindow';
import { resolveNames, type Clash } from '../../lib/organize';
import type { OrganizeDestination, OrganizeFileDto, OrganizePlanDto, OrganizePlanFile } from '../../types';
import { deferred } from '../fakeSignalR';
import { organizeFile as file } from '../fixtures';

const api = vi.hoisted(() => ({ planOrganize: vi.fn(), browseFolders: vi.fn() }));
vi.mock('../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../lib/apiClient')>()), ...api }));

const ROOT = 'C:\\Users\\jamie\\Phone Pictures';
const from = (id: string, name: string, folder = ROOT, changes: Partial<OrganizeFileDto> = {}) => file({ id, name, folder, date: '2023-06-09T14:03:22', ...changes });
const a = from('a', 'IMG_6435.JPG', `${ROOT}\\2023`), b = from('b', 'MOV_8203.MP4', ROOT, { isVideo: true, sizeBytes: 102_760_448, width: 1920, height: 1080 });
const c = from('c', 'IMG_7.JPG', `${ROOT}\\Camera Roll`, { date: '2021-01-02T10:00:00' }), d = from('d', 'IMG_8.JPG', ROOT, { date: '2022-03-04T10:00:00' });
const FILES = [a, b, c, d];
const there = (name: string, changes: Partial<OrganizePlanDto['clashes'][number]['existing'][number]> = {}) =>
  ({ id: `disk-${name}`, name, sizeBytes: 2_097_152, date: '2023-05-17T10:00:00', width: 3024, height: 4032, isVideo: false, placeName: null, identical: false, ...changes });

/** The subfolder the folder in these tests is split into, unless a test says otherwise: one for each year. */
const byYear = (f: OrganizeFileDto) => f.date.slice(0, 4);
/** What the preview asks a plan for: each file with its year as its subfolder, or with none. */
const asked = (...ids: string[]) => ids.map(id => ({ id, subfolder: byYear(FILES.find(f => f.id === id)!) }));
const flat = (...ids: string[]) => ids.map(id => ({ id }));

/** What the service would plan: every file to the subfolder asked for it in the folder asked for (given only its id, to its year's), with whatever is said to be there already. */
function planned(destination: OrganizeDestination, ids: (string | OrganizePlanFile)[], changes: Partial<OrganizePlanDto> = {}, files: OrganizeFileDto[] = FILES): OrganizePlanDto {
  const folder = destination.direct ? destination.basePath : `${destination.basePath}\\${destination.folderName}`;
  const items = ids.map(x => {
    const id = typeof x === 'string' ? x : x.id, subfolder = typeof x === 'string' ? byYear(files.find(f => f.id === id)!) : x.subfolder;
    return { id, folder: subfolder ? `${folder}\\${subfolder}` : folder };
  });
  return { destination: folder, exists: false, items, clashes: [], taken: {}, companions: 0, ...changes };
}
/** A plan in which two names are taken in the folder for 2023: one by the very same file, one by two others. */
const clashing = (destination: OrganizeDestination, ids: (string | OrganizePlanFile)[], files: OrganizeFileDto[] = FILES) => planned(destination, ids, {
  exists: true,
  clashes: [
    { id: 'a', existing: [there('IMG_6435.JPG'), there('IMG_6435 (1).JPG', { date: '2022-05-06T10:00:00' })], nextFree: 'IMG_6435 (2).JPG' },
    { id: 'b', existing: [there('MOV_8203.MP4', { identical: true, isVideo: true, sizeBytes: 102_760_448, date: '2023-06-09T14:03:22', width: 1920, height: 1080, placeName: 'Anaconda, Montana, US' })], nextFree: 'MOV_8203 (1).MP4' },
  ],
  taken: { [`${destination.basePath}\\${destination.folderName}\\2023`]: ['IMG_6435.JPG', 'IMG_6435 (1).JPG', 'MOV_8203.MP4', 'Other.JPG'] },
}, files);

beforeEach(() => {
  api.planOrganize.mockReset();
  api.browseFolders.mockReset();
  api.planOrganize.mockImplementation(async (destination: OrganizeDestination, ids: OrganizePlanFile[]) => planned(destination, ids));
  api.browseFolders.mockImplementation(async (path?: string) => (path
    ? { path, parent: ROOT, folders: [{ name: 'Camera Roll', path: `${ROOT}\\Camera Roll` }] }
    : { path: null, parent: null, folders: [{ name: 'Pictures', path: ROOT }] }));
});

function Harness({ initial, ...props }: Partial<React.ComponentProps<typeof MovePreview>> & { readonly initial?: string }) {
  const [name, setName] = React.useState(initial ?? 'Anaconda');
  const [mode, setMode] = React.useState<'move' | 'copy'>('move');
  const [companions, setCompanions] = React.useState(true);
  return (
    <MovePreview kind="Suggested folder" root={ROOT} files={FILES} name={name} split="year" subfolderOf={byYear} merge="" mode={mode} companions={companions} busy={false} asking={false} placeOf={() => undefined}
      onName={setName} onMode={setMode} onCompanions={setCompanions} onMove={vi.fn()} onDelete={vi.fn()} onClose={vi.fn()} {...props} />
  );
}

function open(props: Partial<React.ComponentProps<typeof MovePreview>> & { initial?: string } = {}) {
  const onMove = vi.fn(), onClose = vi.fn(), onDelete = vi.fn();
  const view = render(<Harness onMove={onMove} onClose={onClose} onDelete={onDelete} {...props} />);
  // The preview as it would be shown next, with something about it changed.
  const show = (changes: Partial<React.ComponentProps<typeof MovePreview>>) => view.rerender(<Harness onMove={onMove} onClose={onClose} onDelete={onDelete} {...props} {...changes} />);
  return { onMove, onClose, onDelete, show, ...view };
}

const preview = () => screen.getByRole('dialog', { name: 'Preview' });
const side = () => preview().querySelector('aside')!;
const button = (name: string | RegExp) => within(preview()).getByRole('button', { name });
const tiles = () => within(within(preview()).getByRole('list', { name: 'Files in this folder' })).getAllByRole('button');
const destination = () => within(preview()).getByLabelText('Destination');
const moveButton = () => within(side()).getByRole('button', { name: /^(Move|Copy) [\d,]+ files?$|^(Moving|Copying)…$/ });
const ready = () => waitFor(() => expect(moveButton()).toBeEnabled());
const clashWindow = () => screen.getByRole('dialog', { name: /^Name already taken/ });
const option = (name: string) => within(clashWindow()).getByRole('radio', { name });
const bins = () => within(clashWindow()).getAllByRole('button', { name: /^Delete / });
const bin = (name: string) => within(clashWindow()).getByRole('button', { name });

describe('the preview', () => {
  it('shows every file that would go, where to, and what it adds up to', async () => {
    open({ merge: 'Includes Opportunity (6 mi away)' });
    expect(preview()).toHaveTextContent(/^PreviewSuggested folderNothing has moved yetEsc×4 files will move/);
    expect(moveButton()).toBeDisabled();
    await ready();
    expect(api.planOrganize).toHaveBeenCalledExactlyOnceWith({ basePath: ROOT, folderName: 'Anaconda', direct: false }, asked('a', 'b', 'c', 'd'));
    expect(tiles().map(t => [t.textContent, t.getAttribute('aria-pressed')])).toEqual([
      ['✓IMG_6435.JPG2023', 'true'], ['✓MOV_8203.MP4Top level', 'true'], ['✓IMG_7.JPGCamera Roll', 'true'], ['✓IMG_8.JPGTop level', 'true'],
    ]);
    expect(within(side()).getByRole('textbox')).toHaveValue('Anaconda');
    expect(side()).toHaveTextContent('Rename it here. Use \\ to make subfolders.');
    expect(side()).toHaveTextContent('The files leave their current folders. Nothing is copied.');
    expect(destination()).toHaveTextContent(`${ROOT}\\Anaconda\\`);
    expect(side()).toHaveTextContent('Split into a subfolder for each year: 2021, 2022, 2023. A new folder is made.');
    expect(side()).toHaveTextContent('Files4 filesSize115.4 MBTakenJan 2021 – Jun 2023FromTop level220231Camera Roll1');
    expect(side()).toHaveTextContent('Includes Opportunity (6 mi away)');
    expect(side()).not.toHaveTextContent('Name already taken');
    expect(side()).not.toHaveTextContent('Live Photo');
    expect(moveButton()).toHaveTextContent('Move 4 files');
    expect(side()).toHaveTextContent('You can undo this afterwards from Recently moved.');
  });

  it('carries the move out with the files that are left in, each under the name it arrives by', async () => {
    const { onMove } = open();
    await ready();
    await userEvent.click(tiles()[2]);
    expect(tiles()[2]).toHaveAttribute('aria-pressed', 'false');
    expect(tiles()[2]).toHaveClass('opacity-35');
    expect(preview()).toHaveTextContent('3 of 4 files will move');
    expect(side()).toHaveTextContent('Files3 files');
    expect(side()).toHaveTextContent('Split into a subfolder for each year: 2022, 2023.');
    await userEvent.click(moveButton());
    expect(onMove).toHaveBeenCalledExactlyOnceWith({
      basePath: ROOT, folderName: 'Anaconda', direct: false, mode: 'move', companions: true, label: 'Anaconda',
      files: [{ id: 'a', name: 'IMG_6435.JPG', subfolder: '2023' }, { id: 'b', name: 'MOV_8203.MP4', subfolder: '2023' }, { id: 'd', name: 'IMG_8.JPG', subfolder: '2022' }],
    }, [a, b, d]);
  });

  it('leaves everything out or puts everything back in at once', async () => {
    open();
    await ready();
    await userEvent.click(button('Leave all out'));
    expect(preview()).toHaveTextContent('0 of 4 files will move');
    expect(moveButton()).toBeDisabled();
    expect(side()).toHaveTextContent('Files0 filesSize1 KBTaken–From');
    await userEvent.click(tiles()[0]);
    expect(preview()).toHaveTextContent('1 of 4 files will move');
    await userEvent.click(button('Include all'));
    expect(preview()).toHaveTextContent('4 files will move');
  });

  it('copies instead when asked, saying what that costs', async () => {
    const { onMove } = open({ files: [a] });
    await ready();
    expect(preview()).toHaveTextContent('1 file will move');
    await userEvent.click(within(screen.getByRole('group', { name: 'Move or copy' })).getByRole('button', { name: 'Copy' }));
    expect(preview()).toHaveTextContent('1 file will be copied');
    expect(side()).toHaveTextContent('The originals stay where they are. The copies use 5.8 MB more space.');
    expect(moveButton()).toHaveTextContent('Copy 1 file');
    await userEvent.click(moveButton());
    expect(onMove.mock.calls[0][0]).toMatchObject({ mode: 'copy' });
  });

  it('says what it is doing while it does it, and lets nothing interrupt', async () => {
    const { onClose, rerender, container } = open({ busy: true });
    await waitFor(() => expect(side()).toHaveTextContent('A new folder is made.'));
    expect(moveButton()).toHaveTextContent('Moving…');
    expect(moveButton()).toBeDisabled();
    expect(button('Cancel')).toBeDisabled();
    fireEvent.keyDown(window, { key: 'Escape' });
    fireEvent.mouseDown(container.firstElementChild!);
    await userEvent.click(button('Close'));
    expect(onClose).not.toHaveBeenCalled();
    rerender(<Harness busy mode="copy" onClose={onClose} />);
    expect(moveButton()).toHaveTextContent('Copying…');
  });

  it('closes on Cancel, on Escape and on a press outside it', async () => {
    const { onClose, container } = open();
    await ready();
    await userEvent.click(button('Cancel'));
    fireEvent.keyDown(window, { key: 'Escape' });
    fireEvent.keyDown(window, { key: 'a' });
    fireEvent.mouseDown(container.firstElementChild!);
    expect(onClose).toHaveBeenCalledTimes(3);
  });

  it('takes a new name for the folder, and plans again for it', async () => {
    open({ split: undefined, subfolderOf: undefined, kind: 'Your folder' });
    await ready();
    expect(preview()).toHaveTextContent('Your folder');
    expect(side()).not.toHaveTextContent('Split into');
    const name = within(side()).getByRole('textbox');
    await userEvent.clear(name);
    // No name, or one a folder cannot have: nothing is planned and nothing can be moved.
    expect(within(side()).getByRole('alert')).toHaveTextContent('Give the new folder a name.');
    expect(within(side()).queryByRole('status')).not.toBeInTheDocument();
    expect(name).toHaveAttribute('aria-invalid', 'true');
    expect(moveButton()).toBeDisabled();
    expect(destination()).toHaveTextContent(`${ROOT}\\`);
    await userEvent.type(name, 'What?');
    expect(within(side()).getByRole('alert')).toHaveTextContent('Folder names cannot contain < > : " / | ? or *');
    await userEvent.clear(name);
    await userEvent.type(name, 'Trips\\Glacier 2022');
    await ready();
    expect(api.planOrganize).toHaveBeenLastCalledWith({ basePath: ROOT, folderName: 'Trips\\Glacier 2022', direct: false }, flat('a', 'b', 'c', 'd'));
    expect(destination()).toHaveTextContent(`${ROOT}\\Trips\\Glacier 2022\\`);
    expect(within(side()).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('says what a folder is split by inside, naming the first few subfolders and what has none', async () => {
    const towns = ['Anaconda', 'Butte', 'Dillon', 'Ennis', 'Havre', 'Libby', 'Polson', 'Sidney'];
    const spread = towns.map((town, i) => from(`t${i}`, `IMG_${i}.JPG`, ROOT, { place: i }));
    const nowhere = [from('n1', 'IMG_n1.JPG'), from('n2', 'IMG_n2.JPG')];
    const byPlace = (f: OrganizeFileDto) => (f.place == null ? undefined : towns[f.place]);
    api.planOrganize.mockImplementation(async (dest: OrganizeDestination, ids: OrganizePlanFile[]) => planned(dest, ids, {}, [...spread, ...nowhere]));
    const { onMove } = open({ initial: '2023', files: [...spread, ...nowhere], split: 'place', subfolderOf: byPlace });
    await ready();
    expect(api.planOrganize).toHaveBeenLastCalledWith({ basePath: ROOT, folderName: '2023', direct: false }, [...towns.map((town, i) => ({ id: `t${i}`, subfolder: town })), { id: 'n1' }, { id: 'n2' }]);
    expect(side()).toHaveTextContent('Split into a subfolder for each place: Anaconda, Butte, Dillon, Ennis, Havre, Libby and 2 more. 2 files with no location go into the folder itself. A new folder is made.');
    // One file with no place left, then none.
    await userEvent.click(tiles()[9]);
    expect(side()).toHaveTextContent('and 2 more. 1 file with no location goes into the folder itself.');
    await userEvent.click(tiles()[8]);
    expect(side()).not.toHaveTextContent('with no location');
    await userEvent.click(tiles()[7]);
    expect(side()).toHaveTextContent('Libby and 1 more.');
    await userEvent.click(moveButton());
    expect(onMove.mock.calls[0][0].files.slice(0, 2)).toEqual([{ id: 't0', name: 'IMG_0.JPG', subfolder: 'Anaconda' }, { id: 't1', name: 'IMG_1.JPG', subfolder: 'Butte' }]);
  });

  it('shows where the files would go even before the plan for it has come back', async () => {
    const waiting = deferred<OrganizePlanDto>();
    api.planOrganize.mockReturnValue(waiting.promise);
    open();
    expect(destination()).toHaveTextContent(`${ROOT}\\Anaconda\\`);
    expect(side()).toHaveTextContent('Split into a subfolder for each year: 2021, 2022, 2023.');
    expect(side()).not.toHaveTextContent('A new folder is made.');
    // Meanwhile it says what it is waiting for.
    expect(within(side()).getByRole('status')).toHaveTextContent('Checking what is already there…');
    await act(async () => waiting.resolve(planned({ basePath: ROOT, folderName: 'Anaconda', direct: false }, ['a', 'b', 'c', 'd'], { exists: true })));
    expect(side()).toHaveTextContent('This folder already exists, so the files are added to it.');
    expect(within(side()).queryByRole('status')).not.toBeInTheDocument();
  });

  it('keeps only the answer to the latest question, and says why when the service cannot plan', async () => {
    const first = deferred<OrganizePlanDto>(), second = deferred<OrganizePlanDto>();
    api.planOrganize.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
    open({ initial: 'A' });
    fireEvent.change(within(side()).getByRole('textbox'), { target: { value: 'AB' } });
    // The answer for "A" arrives late, and so does its failure in another run: neither is shown.
    await act(async () => first.resolve(planned({ basePath: ROOT, folderName: 'A', direct: false }, ['a'], { exists: true })));
    expect(moveButton()).toBeDisabled();
    await act(async () => second.reject(new Error('Folder not found: C:\\gone')));
    expect(within(side()).getByRole('alert')).toHaveTextContent('Folder not found: C:\\gone');
    expect(within(side()).queryByRole('status')).not.toBeInTheDocument();
    expect(side()).not.toHaveTextContent('Split into');
    expect(moveButton()).toBeDisabled();

    const third = deferred<OrganizePlanDto>(), fourth = deferred<OrganizePlanDto>(), fifth = deferred<OrganizePlanDto>();
    api.planOrganize.mockReturnValueOnce(third.promise).mockReturnValueOnce(fourth.promise).mockReturnValueOnce(fifth.promise);
    fireEvent.change(within(side()).getByRole('textbox'), { target: { value: 'ABC' } });
    fireEvent.change(within(side()).getByRole('textbox'), { target: { value: 'ABCD' } });
    await act(async () => third.reject(new Error('late')));
    expect(within(side()).queryByRole('alert')).not.toBeInTheDocument();
    await act(async () => fourth.reject(new TypeError('Failed to fetch')));
    expect(within(side()).getByRole('alert')).toHaveTextContent('Cannot reach the PhotoSense server.');
    fireEvent.change(within(side()).getByRole('textbox'), { target: { value: 'ABCDE' } });
    await act(async () => fifth.reject('refused'));
    expect(within(side()).getByRole('alert')).toHaveTextContent('refused');
  });

  it('leaves alone a file that is already where it would go', async () => {
    api.planOrganize.mockImplementation(async (dest: OrganizeDestination, ids: OrganizePlanFile[]) => planned(dest, ids.filter(f => f.id !== 'c')));
    const { onMove } = open();
    await ready();
    expect(preview()).toHaveTextContent('3 files will move');
    expect(tiles()[2]).toBeDisabled();
    expect(tiles()[2]).toHaveTextContent('ALREADY THEREIMG_7.JPGCamera Roll');
    expect(tiles()[2]).toHaveAttribute('aria-pressed', 'false');
    await userEvent.click(button('Leave all out'));
    await userEvent.click(button('Include all'));
    await userEvent.click(moveButton());
    expect(onMove.mock.calls[0][1]).toEqual([a, b, d]);
  });

  it('shows a hundred files to a page', async () => {
    const many = Array.from({ length: 230 }, (_, i) => from(`m${i}`, `IMG_${i}.JPG`, `${ROOT}\\f${i % 7}`));
    api.planOrganize.mockImplementation(async (dest: OrganizeDestination, ids: OrganizePlanFile[]) => planned(dest, ids, {}, many));
    open({ files: many });
    await ready();
    expect(tiles()).toHaveLength(100);
    expect(preview()).toHaveTextContent('Page 1 of 3');
    await userEvent.click(button('Next'));
    await userEvent.click(button('Next'));
    expect(tiles()).toHaveLength(30);
    expect(tiles()[0]).toHaveTextContent('IMG_200.JPG');
    // Where they come from: the four folders with the most, and how many more there are.
    expect(side()).toHaveTextContent('Fromf033f133f233f333and 3 more folders');
  });

  it('counts one more folder as one', async () => {
    const five = Array.from({ length: 5 }, (_, i) => from(`m${i}`, `IMG_${i}.JPG`, `${ROOT}\\f${i}`));
    api.planOrganize.mockImplementation(async (dest: OrganizeDestination, ids: OrganizePlanFile[]) => planned(dest, ids, {}, five));
    open({ files: five });
    await ready();
    expect(side()).toHaveTextContent('and 1 more folder');
    expect(side()).not.toHaveTextContent('more folders');
  });

  it('offers to bring Live Photo videos and edit files along when there are any', async () => {
    api.planOrganize.mockImplementation(async (dest: OrganizeDestination, ids: OrganizePlanFile[]) => planned(dest, ids, { companions: 1234 }));
    const { onMove } = open();
    await ready();
    const bring = within(side()).getByRole('checkbox');
    expect(bring).toBeChecked();
    expect(side()).toHaveTextContent('Bring Live Photo videos and edit files along with their photos · 1,234 found');
    await userEvent.click(bring);
    expect(bring).not.toBeChecked();
    await userEvent.click(moveButton());
    expect(onMove.mock.calls[0][0]).toMatchObject({ companions: false });
  });
});

describe('where the files go', () => {
  const picker = () => screen.getByRole('dialog', { name: 'Choose where the files go' });

  it('can be another folder, with a new folder made in it or the files put straight into it', async () => {
    const { onMove, onClose } = open();
    await ready();
    expect(within(side()).queryByRole('button', { name: 'Use the default' })).not.toBeInTheDocument();
    await userEvent.click(button('Change'));
    await userEvent.click(await within(picker()).findByRole('button', { name: 'Camera Roll' }));
    // Esc belongs to the folder browser while it is open.
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog', { name: 'Choose where the files go' })).not.toBeInTheDocument();

    await userEvent.click(button('Change'));
    await userEvent.click(await within(picker()).findByRole('button', { name: 'Camera Roll' }));
    await waitFor(() => expect(within(picker()).getByRole('button', { name: 'Make the new folder here' })).toBeEnabled());
    await userEvent.click(within(picker()).getByRole('button', { name: 'Make the new folder here' }));
    await ready();
    expect(destination()).toHaveTextContent(`${ROOT}\\Camera Roll\\Anaconda\\`);
    expect(api.planOrganize).toHaveBeenLastCalledWith({ basePath: `${ROOT}\\Camera Roll`, folderName: 'Anaconda', direct: false }, asked('a', 'b', 'c', 'd'));

    await userEvent.click(button('Change'));
    await waitFor(() => expect(within(picker()).getByRole('button', { name: 'Put the files here' })).toBeEnabled());
    await userEvent.click(within(picker()).getByRole('button', { name: 'Put the files here' }));
    await ready();
    expect(side()).toHaveTextContent('The files go straight into Camera Roll, without a new folder.');
    expect(within(side()).queryByRole('textbox')).not.toBeInTheDocument();
    expect(destination()).toHaveTextContent(`${ROOT}\\Camera Roll\\`);
    expect(api.planOrganize).toHaveBeenLastCalledWith({ basePath: `${ROOT}\\Camera Roll`, folderName: '', direct: true }, asked('a', 'b', 'c', 'd'));
    await userEvent.click(moveButton());
    expect(onMove.mock.calls[0][0]).toMatchObject({ basePath: `${ROOT}\\Camera Roll`, folderName: '', direct: true, label: 'Camera Roll' });

    await userEvent.click(button('Make a new folder there instead'));
    await ready();
    expect(destination()).toHaveTextContent(`${ROOT}\\Camera Roll\\Anaconda\\`);
    await userEvent.click(button('Use the default'));
    await ready();
    expect(destination()).toHaveTextContent(`${ROOT}\\Anaconda\\`);
    expect(within(side()).queryByRole('button', { name: 'Use the default' })).not.toBeInTheDocument();
  });

  it('shows the way the files would go into a folder chosen before the plan is back', async () => {
    open();
    await ready();
    api.planOrganize.mockReturnValue(new Promise(() => undefined));
    await userEvent.click(button('Change'));
    await waitFor(() => expect(within(picker()).getByRole('button', { name: 'Put the files here' })).toBeEnabled());
    await userEvent.click(within(picker()).getByRole('button', { name: 'Put the files here' }));
    expect(destination()).toHaveTextContent(`${ROOT}\\`);
    await userEvent.click(button('Change'));
    await userEvent.click(within(picker()).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog', { name: 'Choose where the files go' })).not.toBeInTheDocument();
  });
});

describe('names already taken', () => {
  beforeEach(() => { api.planOrganize.mockImplementation(async (dest: OrganizeDestination, ids: OrganizePlanFile[]) => clashing(dest, ids)); });
  const box = () => within(side()).getByText('Name already taken').parentElement!;

  it('are counted in the preview, with what will happen to each', async () => {
    open();
    await ready();
    expect(box()).toHaveTextContent('Name already taken2 files have the same name as a file already there. Nothing is replaced.2 get a numberIMG_6435.JPG → IMG_6435 (2).JPGMOV_8203.MP4 → MOV_8203 (1).MP4Review each name');
    expect(tiles().map(t => within(t).queryByText('NAME TAKEN') !== null)).toEqual([true, true, false, false]);
    expect(side()).toHaveTextContent('This folder already exists, so the files are added to it.');
  });

  it('counts a single one as one, and lists only the first three of many', async () => {
    api.planOrganize.mockImplementation(async (dest: OrganizeDestination, ids: OrganizePlanFile[]) => planned(dest, ids, { taken: { [`${ROOT}\\Anaconda\\2023`]: ['IMG_6435.JPG'] } }));
    const { unmount } = open();
    await ready();
    expect(box()).toHaveTextContent('1 file has the same name as a file already there.');
    expect(box()).not.toHaveTextContent('more');
    unmount();

    const five = Array.from({ length: 5 }, (_, i) => from(`m${i}`, `IMG_${i}.JPG`));
    api.planOrganize.mockImplementation(async (dest: OrganizeDestination, ids: OrganizePlanFile[]) => planned(dest, ids, { taken: { [`${ROOT}\\Anaconda\\2023`]: five.map(f => f.name) } }, five));
    open({ files: five });
    await ready();
    expect(box()).toHaveTextContent('IMG_2.JPG → IMG_2 (1).JPGand 2 more');
  });

  it('are gone through one by one: a number, another name, or left where it is', async () => {
    const { onMove, onClose } = open({ placeOf: id => (id === 'a' ? 'Anaconda, Montana' : undefined) });
    await ready();
    await userEvent.click(within(box()).getByRole('button', { name: 'Review each name' }));
    expect(clashWindow()).toHaveTextContent(/^Name already takenIMG_6435\.JPGSide by sideMoving fileAlready therePrevious1 of 2Next/);
    expect(clashWindow()).toHaveTextContent('IMG_6435.JPG is going to Anaconda\\2023, which already has a file with this name.');
    expect(clashWindow()).toHaveTextContent('IMG_6435 (1).JPG is also there, so the next free number is (2).');
    expect(option('Add a number')).toBeChecked();
    expect(clashWindow()).toHaveTextContent('Add a numberIMG_6435 (2).JPG');

    // Another name, offered from when the file was taken; the extension stays.
    await userEvent.click(option('Rename it'));
    const name = within(clashWindow()).getByRole('textbox', { name: 'New name' });
    expect(name).toHaveValue('IMG_6435 2023-06');
    expect(clashWindow()).toHaveTextContent('.JPG');
    await userEvent.clear(name);
    expect(within(clashWindow()).getByRole('alert')).toHaveTextContent('That name is taken or has characters file names cannot use. It will get a number instead.');
    await userEvent.type(name, 'Other');
    expect(name).toHaveAttribute('aria-invalid', 'true');
    await userEvent.type(name, ' one');
    expect(within(clashWindow()).queryByRole('alert')).not.toBeInTheDocument();
    // Esc belongs to this window, and typing a space or an arrow in the name belongs to the name.
    fireEvent.keyDown(name, { key: 'Escape' });
    fireEvent.keyDown(name, { key: 'ArrowRight' });
    expect(clashWindow()).toHaveTextContent('1 of 2');

    await userEvent.click(within(clashWindow()).getByRole('button', { name: 'Next name · 2 of 2' }));
    expect(clashWindow()).toHaveTextContent('These two files are byte-for-byte the same. Leaving this one where it is avoids keeping a duplicate.');
    await userEvent.click(option('Leave it where it is'));
    expect(clashWindow()).toHaveTextContent('It stays in Top level and is not moved.');
    await userEvent.click(within(clashWindow()).getByRole('button', { name: 'Done' }));
    expect(screen.queryByRole('dialog', { name: /^Name already taken/ })).not.toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();

    expect(box()).toHaveTextContent('1 renamed · 1 left where it isIMG_6435.JPG → Other one.JPGMOV_8203.MP4 → stays where it is');
    expect(tiles()[1]).toHaveAttribute('aria-pressed', 'false');
    await userEvent.click(moveButton());
    expect(onMove.mock.calls[0][0].files).toEqual([{ id: 'a', name: 'Other one.JPG', subfolder: '2023' }, { id: 'c', name: 'IMG_7.JPG', subfolder: '2021' }, { id: 'd', name: 'IMG_8.JPG', subfolder: '2022' }]);
  });

  it('keep the name typed for a file when its choice is looked at again, and go back to a number when asked', async () => {
    open();
    await ready();
    await userEvent.click(within(box()).getByRole('button', { name: 'Review each name' }));
    await userEvent.click(option('Rename it'));
    fireEvent.change(within(clashWindow()).getByRole('textbox', { name: 'New name' }), { target: { value: 'Mine' } });
    // Picking the option that is already on changes nothing, and neither does a click in the name itself.
    await userEvent.click(option('Rename it'));
    await userEvent.click(within(clashWindow()).getByRole('textbox', { name: 'New name' }));
    expect(within(clashWindow()).getByRole('textbox', { name: 'New name' })).toHaveValue('Mine');
    await userEvent.click(option('Leave it where it is'));
    await userEvent.click(within(clashWindow()).getByText('Rename it'));
    expect(within(clashWindow()).getByRole('textbox', { name: 'New name' })).toHaveValue('Mine');
    await userEvent.click(within(clashWindow()).getByText('Add a number'));
    expect(option('Add a number')).toBeChecked();
    await userEvent.click(within(clashWindow()).getByRole('button', { name: 'Back to preview' }));
    expect(box()).toHaveTextContent('2 get a number');
  });

  it('can all be given a number from here on at once', async () => {
    open();
    await ready();
    await userEvent.click(within(box()).getByRole('button', { name: 'Review each name' }));
    await userEvent.click(within(clashWindow()).getByRole('button', { name: 'Next' }));
    // The second has had a name typed for it and then been left out: both are forgotten when it is given a number.
    await userEvent.click(option('Rename it'));
    await userEvent.click(option('Leave it where it is'));
    await userEvent.click(within(clashWindow()).getByRole('button', { name: 'Previous' }));
    await userEvent.click(option('Leave it where it is'));
    expect(within(clashWindow()).getByRole('button', { name: 'Previous' })).toBeDisabled();
    await userEvent.click(within(clashWindow()).getByRole('button', { name: 'Add a number to all the remaining names' }));
    expect(screen.queryByRole('dialog', { name: /^Name already taken/ })).not.toBeInTheDocument();
    // The one being looked at keeps its choice; the ones after it get a number.
    expect(box()).toHaveTextContent('1 gets a number · 1 left where it isIMG_6435.JPG → stays where it isMOV_8203.MP4 → MOV_8203 (1).MP4');
  });

  it('close their window when the names stop being taken, as when the folder is renamed', async () => {
    open();
    await ready();
    await userEvent.click(within(box()).getByRole('button', { name: 'Review each name' }));
    await userEvent.click(within(clashWindow()).getByRole('button', { name: 'Next' }));
    expect(clashWindow()).toHaveTextContent('2 of 2');
    // The second of the two is left out by clicking its tile... it is still a name that is taken, and stays in the list.
    api.planOrganize.mockImplementation(async (dest: OrganizeDestination, ids: OrganizePlanFile[]) => planned(dest, ids, {
      clashes: [{ id: 'a', existing: [there('IMG_6435.JPG')], nextFree: 'IMG_6435 (1).JPG' }], taken: { [`${ROOT}\\Elsewhere\\2023`]: ['IMG_6435.JPG'] },
    }));
    fireEvent.change(within(side()).getByRole('textbox'), { target: { value: 'Elsewhere' } });
    // While the new plan is awaited the names stay as they were; when it comes there is one name, not two.
    await waitFor(() => expect(clashWindow()).toHaveTextContent('1 of 1'));
    api.planOrganize.mockImplementation(async (dest: OrganizeDestination, ids: OrganizePlanFile[]) => planned(dest, ids));
    fireEvent.change(within(side()).getByRole('textbox'), { target: { value: 'Nowhere' } });
    await ready();
    expect(screen.queryByRole('dialog', { name: /^Name already taken/ })).not.toBeInTheDocument();
  });

  it('hand on the wish to delete one of the two files, and go on to the next name once it has gone', async () => {
    const { onDelete, onClose, show } = open();
    await ready();
    await userEvent.click(within(box()).getByRole('button', { name: 'Review each name' }));
    expect(bins().map(x => x.getAttribute('aria-label'))).toEqual(['Delete IMG_6435.JPG, moving', 'Delete IMG_6435.JPG, already there', 'Delete IMG_6435 (1).JPG, already there']);
    await userEvent.click(bin('Delete IMG_6435.JPG, moving'));
    expect(onDelete).toHaveBeenCalledExactlyOnceWith(
      { id: 'a', name: 'IMG_6435.JPG', sizeBytes: 6_093_000, folder: `${ROOT}\\2023` }, { id: 'disk-IMG_6435.JPG', name: 'IMG_6435.JPG', sizeBytes: 2_097_152, folder: `${ROOT}\\Anaconda\\2023` });

    // While the question about it is up, and while it is being carried out, neither window does anything: Esc is the question's.
    for (const held of [{ asking: true }, { busy: true }]) {
      show(held);
      expect(bins().every(x => (x as HTMLButtonElement).disabled)).toBe(true);
      fireEvent.keyDown(window, { key: 'Escape' });
      fireEvent.keyDown(window, { key: 'ArrowRight' });
      expect(clashWindow()).toHaveTextContent('1 of 2');
    }
    expect(onClose).not.toHaveBeenCalled();

    // The files that had its name have gone from the folder. While what is there is asked about again, the name stays in view, with nothing to press.
    const asking = deferred<void>();
    api.planOrganize.mockImplementation(async (dest: OrganizeDestination, ids: OrganizePlanFile[]) => {
      await asking.promise;
      const all = clashing(dest, ids);
      return { ...all, clashes: all.clashes.slice(1), taken: { [`${ROOT}\\Anaconda\\2023`]: ['MOV_8203.MP4'] } };
    });
    show({ subfolderOf: f => byYear(f) });
    expect(within(side()).getByRole('status')).toHaveTextContent('Checking what is already there…');
    expect(clashWindow()).toHaveAccessibleName('Name already taken: IMG_6435.JPG');
    expect(clashWindow()).toHaveTextContent('1 of 2');
    expect(bins().every(x => (x as HTMLButtonElement).disabled)).toBe(true);
    await act(async () => asking.resolve());
    // The next name has taken its place.
    await waitFor(() => expect(clashWindow()).toHaveAccessibleName('Name already taken: MOV_8203.MP4'));
    expect(clashWindow()).toHaveTextContent('1 of 1');
    expect(bins().map(x => [x.getAttribute('aria-label'), (x as HTMLButtonElement).disabled])).toEqual([['Delete MOV_8203.MP4, moving', false], ['Delete MOV_8203.MP4, already there', false]]);

    // With the file being moved gone as well, the last of the names, the window closes; it does not come back by itself should a name be taken again.
    await userEvent.click(bin('Delete MOV_8203.MP4, moving'));
    expect(onDelete).toHaveBeenLastCalledWith(expect.objectContaining({ id: 'b', folder: ROOT }), expect.objectContaining({ id: 'disk-MOV_8203.MP4' }));
    api.planOrganize.mockImplementation(async (dest: OrganizeDestination, ids: OrganizePlanFile[]) => planned(dest, ids));
    show({ files: [a, c, d] });
    await ready();
    expect(screen.queryByRole('dialog', { name: /^Name already taken/ })).not.toBeInTheDocument();
    api.planOrganize.mockImplementation(async (dest: OrganizeDestination, ids: OrganizePlanFile[]) => clashing(dest, ids));
    show({ files: FILES });
    await ready();
    expect(box()).toHaveTextContent('2 files have the same name');
    expect(screen.queryByRole('dialog', { name: /^Name already taken/ })).not.toBeInTheDocument();
  });

  it('do not close the preview on Escape while a question is up over it', async () => {
    const { onClose, show } = open({ asking: true });
    await ready();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).not.toHaveBeenCalled();
    show({ asking: false });
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledOnce();
  });
});

describe('the name already taken window', () => {
  const DEST = `${ROOT}\\Anaconda\\2023`;
  function clashes(choices: Parameters<typeof resolveNames>[2] = {}, excluded: Record<string, boolean> = {}): Clash[] {
    const dest = { basePath: ROOT, folderName: 'Anaconda', direct: false };
    const three = [a, b, from('e', 'img_6435.jpg', `${ROOT}\\Old`, { width: 0, height: 0 })];
    return resolveNames(three, clashing(dest, ['a', 'b', 'e'], three), choices, excluded).clashes;
  }
  function show(index: number, props: Partial<React.ComponentProps<typeof NameClashWindow>> = {}, list = clashes()) {
    const handlers = { onIndex: vi.fn(), onResolve: vi.fn(), onWanted: vi.fn(), onNumberTheRest: vi.fn(), onDelete: vi.fn(), onClose: vi.fn() };
    const view = render(<NameClashWindow clashes={list} index={index} root={ROOT} frozen={false} placeOf={() => undefined} {...handlers} {...props} />);
    return { ...handlers, ...view, list };
  }
  const pressed = () => within(screen.getByRole('group', { name: 'View' })).getAllByRole('button').filter(x => x.getAttribute('aria-pressed') === 'true').map(x => x.textContent);
  const rows = () => within(clashWindow()).getAllByRole('row').slice(1).map(r => r.textContent);

  it('shows the file beside every file that has its name, and what differs between the first two', () => {
    show(0, { placeOf: id => (id === 'a' ? 'Anaconda, Montana' : undefined) });
    expect(within(clashWindow()).getAllByRole('figure').map(f => f.textContent)).toEqual([
      'MovingIMG_6435.JPG5.8 MB · Jun 9, 2023', 'Already thereIMG_6435.JPG2.0 MB · May 17, 2023', 'Already thereIMG_6435 (1).JPG2.0 MB · May 6, 2022',
    ]);
    expect(within(clashWindow()).getAllByRole('img').map(i => i.getAttribute('src'))).toEqual([
      'http://localhost:7071/api/organize/files/a/image', 'http://localhost:7071/api/organize/files/disk-IMG_6435.JPG/image', 'http://localhost:7071/api/organize/files/disk-IMG_6435 (1).JPG/image',
    ]);
    expect(rows()).toEqual(['TakenJun 9, 2023May 17, 2023', 'Size5.8 MB2.0 MB', 'Pixels4032 × 30243024 × 4032', 'PlaceAnaconda, MontanaNo location', 'Folder2023Anaconda\\2023']);
    expect(clashWindow()).not.toHaveTextContent('byte-for-byte');
    expect(clashWindow()).toHaveTextContent('Press Space to flip between the two files in the same spot · ← → for the previous or next name');
  });

  it('says a detail is the same when it is, shows a video as a still, and uses what the service knows of a place', () => {
    show(1, { placeOf: () => undefined });
    expect(clashWindow().querySelector('img')).toBeNull();
    expect(rows()).toEqual(['TakenJun 9, 2023Same', 'Size98.0 MBSame', 'Video1920 × 1080Same', 'PlaceNo locationAnaconda, Montana, US', 'FolderTop levelAnaconda\\2023']);
    expect(within(clashWindow()).getAllByRole('row')[1]).not.toHaveClass('bg-diff');
    expect(within(clashWindow()).getAllByRole('row')[4]).toHaveClass('bg-diff');
    expect(clashWindow()).not.toHaveTextContent('is also there');
  });

  it('names where both were taken alike when both are among the folder\'s files', () => {
    show(1, { placeOf: () => 'Anaconda, Montana' });
    expect(rows()[3]).toBe('PlaceAnaconda, MontanaSame');
  });

  it('says so when the name is taken by another file of the same move, and when the size of a picture is not known', () => {
    show(2);
    expect(clashWindow()).toHaveTextContent('img_6435.jpg is going to Anaconda\\2023, and so is another file of this move with the same name.');
    expect(within(clashWindow()).getAllByRole('columnheader').map(h => h.textContent)).toEqual(['What', 'Moving', 'Also moving']);
    expect(within(clashWindow()).getAllByRole('figure')[1]).toHaveTextContent('Also movingIMG_6435 (2).JPG');
    expect(rows()[2]).toBe('PixelsNot known4032 × 3024');
  });

  it('flips between the two files in the same spot, by the buttons or with Space', async () => {
    show(0);
    await userEvent.click(within(clashWindow()).getByRole('button', { name: 'Moving file' }));
    expect(within(clashWindow()).getAllByRole('figure').map(f => f.textContent)).toEqual(['MovingIMG_6435.JPG · 5.8 MB']);
    expect(within(clashWindow()).getByText('Moving', { selector: '.badge' })).toHaveClass('bg-brand');
    // The bin is under whichever file is showing.
    expect(bins().map(x => x.getAttribute('aria-label'))).toEqual(['Delete IMG_6435.JPG, moving']);
    await userEvent.click(within(clashWindow()).getByRole('button', { name: 'Already there' }));
    expect(within(clashWindow()).getAllByRole('figure').map(f => f.textContent)).toEqual(['Already thereIMG_6435.JPG · 2.0 MB']);
    expect(bins().map(x => x.getAttribute('aria-label'))).toEqual(['Delete IMG_6435.JPG, already there']);
    await userEvent.click(within(clashWindow()).getByRole('button', { name: 'Side by side' }));

    fireEvent.keyDown(clashWindow(), { key: ' ' });
    expect(pressed()).toEqual(['Already there']);
    fireEvent.keyDown(window, { key: ' ' });
    expect(pressed()).toEqual(['Moving file']);
    // Space on a button presses the button.
    fireEvent.keyDown(within(clashWindow()).getByRole('button', { name: 'Back to preview' }), { key: ' ' });
    fireEvent.keyDown(clashWindow(), { key: 'x' });
    expect(pressed()).toEqual(['Moving file']);
  });

  it('has a small bin under each file, which asks for that file to go and says which one stays', async () => {
    const { onDelete } = show(0);
    const moving = { id: 'a', name: 'IMG_6435.JPG', sizeBytes: 6_093_000, folder: `${ROOT}\\2023` };
    const first = { id: 'disk-IMG_6435.JPG', name: 'IMG_6435.JPG', sizeBytes: 2_097_152, folder: DEST }, second = { id: 'disk-IMG_6435 (1).JPG', name: 'IMG_6435 (1).JPG', sizeBytes: 2_097_152, folder: DEST };
    expect(bins().map(x => [x.getAttribute('aria-label'), x.getAttribute('title')])).toEqual([
      ['Delete IMG_6435.JPG, moving', 'Delete this file'], ['Delete IMG_6435.JPG, already there', 'Delete this file'], ['Delete IMG_6435 (1).JPG, already there', 'Delete this file'],
    ]);
    // Each is inside the caption of the picture it belongs to.
    expect(within(clashWindow()).getAllByRole('figure').map(f => within(f).getAllByRole('button').length)).toEqual([1, 1, 1]);
    for (const x of bins()) await userEvent.click(x);
    // When the file being moved goes, the file that has its name stays; when any other goes, the file being moved stays.
    expect(onDelete.mock.calls).toEqual([[moving, first], [first, moving], [second, moving]]);
  });

  it('deletes another file of the same move under the name it has now, not the one it would arrive by', async () => {
    const { onDelete } = show(2);
    expect(within(clashWindow()).getAllByRole('figure')[1]).toHaveTextContent('Also movingIMG_6435 (2).JPG');
    expect(bins().map(x => x.getAttribute('aria-label'))).toEqual(['Delete img_6435.jpg, moving', 'Delete IMG_6435.JPG, also moving']);
    await userEvent.click(bin('Delete IMG_6435.JPG, also moving'));
    expect(onDelete).toHaveBeenCalledExactlyOnceWith({ id: 'a', name: 'IMG_6435.JPG', sizeBytes: 6_093_000, folder: `${ROOT}\\2023` }, { id: 'e', name: 'img_6435.jpg', sizeBytes: 6_093_000, folder: `${ROOT}\\Old` });
  });

  it('does nothing while something is being carried out or a question is up over it', async () => {
    const { onIndex, onClose, onDelete } = show(1, { frozen: true });
    expect(bins().every(x => (x as HTMLButtonElement).disabled)).toBe(true);
    for (const x of bins()) await userEvent.click(x);
    for (const key of ['Escape', 'ArrowLeft', 'ArrowRight', ' ']) fireEvent.keyDown(window, { key });
    expect(pressed()).toEqual(['Side by side']);
    expect([onIndex, onClose, onDelete].some(f => f.mock.calls.length > 0)).toBe(false);
  });

  it('steps through the names with the arrow keys and its buttons, and closes on Escape', async () => {
    const { onIndex, onClose, onNumberTheRest, rerender, list, ...rest } = show(1);
    fireEvent.keyDown(window, { key: 'ArrowLeft' });
    fireEvent.keyDown(window, { key: 'ArrowRight' });
    expect(onIndex.mock.calls).toEqual([[0], [2]]);
    await userEvent.click(within(clashWindow()).getByRole('button', { name: 'Previous' }));
    await userEvent.click(within(clashWindow()).getByRole('button', { name: 'Next' }));
    await userEvent.click(within(clashWindow()).getByRole('button', { name: 'Next name · 3 of 3' }));
    expect(onIndex.mock.calls).toEqual([[0], [2], [0], [2], [2]]);
    await userEvent.click(within(clashWindow()).getByRole('button', { name: 'Add a number to all the remaining names' }));
    expect(onNumberTheRest).toHaveBeenCalledOnce();
    fireEvent.keyDown(window, { key: 'Escape' });
    await userEvent.click(within(clashWindow()).getByRole('button', { name: 'Back to preview' }));
    expect(onClose).toHaveBeenCalledTimes(2);

    // At either end the arrows have nowhere to go.
    const again = { ...rest, onIndex, onClose, onNumberTheRest };
    onIndex.mockClear();
    rerender(<NameClashWindow clashes={list} index={0} root={ROOT} frozen={false} placeOf={() => undefined} {...again} />);
    fireEvent.keyDown(window, { key: 'ArrowLeft' });
    rerender(<NameClashWindow clashes={list} index={2} root={ROOT} frozen={false} placeOf={() => undefined} {...again} />);
    fireEvent.keyDown(window, { key: 'ArrowRight' });
    expect(onIndex).not.toHaveBeenCalled();
    expect(within(clashWindow()).getByRole('button', { name: 'Next' })).toBeDisabled();
    expect(within(clashWindow()).queryByRole('button', { name: /remaining names/ })).not.toBeInTheDocument();
    await userEvent.click(within(clashWindow()).getByRole('button', { name: 'Done' }));
    expect(onClose).toHaveBeenCalledTimes(3);
  });

  it('reports what is chosen for the file, and the name typed for it', async () => {
    const list = clashes({ a: { mode: 'rename', name: 'Mine' } }, { b: true });
    const { onResolve, onWanted, rerender, ...handlers } = show(0, {}, list);
    expect([option('Add a number'), option('Rename it'), option('Leave it where it is')].map(o => o.getAttribute('aria-checked'))).toEqual(['false', 'true', 'false']);
    await userEvent.click(option('Add a number'));
    await userEvent.click(option('Leave it where it is'));
    await userEvent.click(option('Rename it'));
    expect(onResolve.mock.calls).toEqual([[list[0], 'auto'], [list[0], 'skip']]);
    fireEvent.change(within(clashWindow()).getByRole('textbox', { name: 'New name' }), { target: { value: 'Mine too' } });
    expect(onWanted).toHaveBeenCalledExactlyOnceWith(list[0], 'Mine too');

    rerender(<NameClashWindow clashes={list} index={1} root={ROOT} frozen={false} placeOf={() => undefined} onResolve={onResolve} onWanted={onWanted} {...handlers} />);
    expect(option('Leave it where it is')).toBeChecked();
    await userEvent.click(option('Rename it'));
    expect(onResolve).toHaveBeenLastCalledWith(list[1], 'rename');
  });
});
