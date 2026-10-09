import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { FileGallery } from '../../components/organize/FileGallery';
import { ReadingOverlay } from '../../components/organize/ReadingOverlay';
import { SelectionBar } from '../../components/organize/SelectionBar';
import { SuggestionsPanel, type OwnFolder } from '../../components/organize/SuggestionsPanel';
import { Check, Pager, Thumb, countFiles } from '../../components/organize/shared';
import { defaultSettings, mapLayout, noChoices, suggest, type OrganizeSettings } from '../../lib/organize';
import type { OrganizeBatchDto, OrganizeFileDto, OrganizeFilesDto } from '../../types';
import { organizeFile as file, organizePlace as place } from '../fixtures';

const API = 'http://localhost:7071/api';
const ROOT = 'C:\\Users\\jamie\\Phone Pictures';
const PLACES = [
  place({ town: 'Anaconda', latitude: 46.1283, longitude: -112.9423 }),
  place({ town: 'West Glacier', area: 'Glacier National Park', latitude: 48.495, longitude: -113.9819 }),
  place({ town: 'Apgar', area: 'Apgar Village', latitude: 48.5268, longitude: -113.9873 }),
];
const at = (id: string, placeIndex: number | null, changes: Partial<OrganizeFileDto> = {}) => file({ id, name: `${id}.JPG`, place: placeIndex, folder: ROOT, date: '2022-07-04T10:00:00', ...changes });
const FILES = [at('a1', 0), at('a2', 0), at('a3', 0), at('a4', 0, { isVideo: true, name: 'a4.MOV', durationSeconds: 14 }), at('a5', 0), at('g1', 1), at('g2', 1), at('p1', 2), at('n1', null)];
const LISTING: OrganizeFilesDto = { root: ROOT, fromScan: true, places: PLACES, files: FILES };
const dragged = () => ({ dataTransfer: { setData: vi.fn(), effectAllowed: '' } });

describe('the small parts', () => {
  it('counts files in words', () => {
    expect([countFiles(1), countFiles(4212)]).toEqual(['1 file', '4,212 files']);
  });

  it('shows a picture by its preview and a video as a dark tile, with its playing time when asked', () => {
    const { container, rerender } = render(<Thumb file={FILES[0]} className="h-full" />);
    const picture = container.querySelector('img')!;
    expect(picture).toHaveAttribute('src', `${API}/organize/files/a1/thumbnail`);
    expect(picture).toHaveAttribute('draggable', 'false');
    expect(picture).toHaveClass('h-full');

    rerender(<Thumb file={FILES[3]} duration />);
    expect(container.querySelector('img')).toBeNull();
    expect(container).toHaveTextContent('0:14');
    rerender(<Thumb file={FILES[3]} />);
    expect(container).toHaveTextContent('');
    rerender(<Thumb file={{ ...FILES[3], durationSeconds: null }} duration play={7} />);
    expect(container).toHaveTextContent('');
  });

  it('ticks a box that is on, in either size', () => {
    const { container, rerender } = render(<Check on />);
    expect(container.firstElementChild).toHaveTextContent('✓');
    expect(container.firstElementChild).toHaveClass('bg-brand', 'h-5');
    rerender(<Check on={false} small />);
    expect(container.firstElementChild).toHaveTextContent('');
    expect(container.firstElementChild).toHaveClass('bg-black/30', 'h-[18px]');
  });

  it('turns pages, and stops at the first and the last', async () => {
    const onPage = vi.fn();
    const { rerender } = render(<Pager page={2} pages={3} onPage={onPage} />);
    expect(screen.getByText('Page 2 of 3')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Prev' }));
    await userEvent.click(screen.getByRole('button', { name: 'Next' }));
    expect(onPage.mock.calls).toEqual([[1], [3]]);
    rerender(<Pager page={1} pages={1} onPage={onPage} />);
    expect(screen.getByRole('button', { name: 'Prev' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled();
  });
});

describe('the file gallery', () => {
  function show(props: Partial<React.ComponentProps<typeof FileGallery>> = {}) {
    const handlers = { onPage: vi.fn(), onFilter: vi.fn(), onClearFocus: vi.fn(), onClearAll: vi.fn(), onPick: vi.fn(), onOpen: vi.fn(), onDragStart: vi.fn(), onDragEnd: vi.fn() };
    const view = render(<FileGallery files={FILES} places={PLACES} page={1} filter="all" counts={{ all: 9, todo: 8, noloc: 1 }} narrowed={false} picked={new Set()} empty="No files here" tagOf={() => undefined} {...handlers} {...props} />);
    return { ...handlers, ...view };
  }
  const tiles = () => within(screen.getByRole('list', { name: 'Files' })).getAllByRole('button');

  it('shows every file with when and where it is from, and what each filter would show', () => {
    const { container } = show();
    expect(container.querySelector('span')).toHaveTextContent(/^9 files$/);
    expect(within(screen.getByRole('group', { name: 'Show' })).getAllByRole('button').map(b => [b.textContent, b.getAttribute('aria-pressed')]))
      .toEqual([['All9', 'true'], ['Not organized8', 'false'], ['No location1', 'false']]);
    expect(tiles().map(t => t.textContent)).toEqual([
      'a1.JPGJul 4, 2022 · Anaconda', 'a2.JPGJul 4, 2022 · Anaconda', 'a3.JPGJul 4, 2022 · Anaconda', '0:14a4.MOVJul 4, 2022 · Anaconda', 'a5.JPGJul 4, 2022 · Anaconda',
      'g1.JPGJul 4, 2022 · West Glacier', 'g2.JPGJul 4, 2022 · West Glacier', 'p1.JPGJul 4, 2022 · Apgar', 'n1.JPGJul 4, 2022 · No location',
    ]);
    expect(screen.getByText('Page 1 of 1')).toBeInTheDocument();
    expect(screen.queryByText('No files here')).not.toBeInTheDocument();
  });

  it('marks the selected files, and tags the ones a folder is arranged for or that have gone to one', () => {
    show({ picked: new Set(['a2']), tagOf: f => (f.id === 'a1' ? { text: 'For Trips', done: false } : f.id === 'a3' ? { text: 'In Anaconda', done: true } : undefined) });
    expect(tiles().map(t => t.getAttribute('aria-pressed')).slice(0, 3)).toEqual(['false', 'true', 'false']);
    expect(within(tiles()[1]).getByText('✓')).toBeInTheDocument();
    expect(within(tiles()[0]).getByText('For Trips')).toHaveClass('bg-[#1c6078]/90');
    expect(within(tiles()[2]).getByText('In Anaconda')).toHaveClass('bg-[#1d6b4c]/90');
  });

  it('selects on a click or a key, a range with Shift held, and opens on a double click', async () => {
    const { onPick, onOpen } = show();
    await userEvent.click(tiles()[1]);
    expect(onPick).toHaveBeenLastCalledWith(FILES[1], 1, false);
    fireEvent.click(tiles()[4], { shiftKey: true });
    expect(onPick).toHaveBeenLastCalledWith(FILES[4], 4, true);
    fireEvent.keyDown(tiles()[2], { key: 'Enter' });
    expect(onPick).toHaveBeenLastCalledWith(FILES[2], 2, false);
    fireEvent.keyDown(tiles()[3], { key: ' ', shiftKey: true });
    expect(onPick).toHaveBeenLastCalledWith(FILES[3], 3, true);
    fireEvent.keyDown(tiles()[3], { key: 'a' });
    expect(onPick).toHaveBeenCalledTimes(4);
    fireEvent.doubleClick(tiles()[0]);
    expect(onOpen).toHaveBeenCalledExactlyOnceWith(FILES[0]);
  });

  it('can be dragged from, carrying the name of the file that was taken hold of', () => {
    const { onDragStart, onDragEnd } = show();
    const drag = dragged();
    fireEvent.dragStart(tiles()[5], drag);
    expect(onDragStart).toHaveBeenCalledExactlyOnceWith(FILES[5]);
    expect(drag.dataTransfer.setData).toHaveBeenCalledExactlyOnceWith('text/plain', 'g1.JPG');
    expect(drag.dataTransfer.effectAllowed).toBe('move');
    fireEvent.dragEnd(tiles()[5]);
    expect(onDragEnd).toHaveBeenCalledOnce();
  });

  it('shows sixty to a page, and counts on from the page before', async () => {
    const many = Array.from({ length: 130 }, (_, i) => at(`f${i}`, 0));
    const { onPage, onPick } = show({ files: many, page: 2 });
    expect(tiles()).toHaveLength(60);
    expect(screen.getByText('Page 2 of 3')).toBeInTheDocument();
    await userEvent.click(tiles()[0]);
    expect(onPick).toHaveBeenLastCalledWith(many[60], 60, false);
    await userEvent.click(screen.getByRole('button', { name: 'Next' }));
    expect(onPage).toHaveBeenCalledExactlyOnceWith(3);
  });

  it('says which folder\'s files alone are showing, with the way back to all of them', async () => {
    const { onClearFocus, onClearAll, onFilter, container } = show({ focus: 'Suggested: Anaconda', narrowed: true, files: [FILES[0]] });
    expect(screen.getByText('Suggested: Anaconda')).toBeInTheDocument();
    // One button puts away whatever keeps files from showing, the folder with the rest.
    expect(screen.getByRole('button', { name: 'Clear all' })).toHaveAttribute('title', 'Show every file again: no folder picked, no search, no filter');
    await userEvent.click(screen.getByRole('button', { name: 'Clear all' }));
    expect(onClearAll).toHaveBeenCalledOnce();
    expect(onClearFocus).not.toHaveBeenCalled();
    expect(container.querySelector('span')).toHaveTextContent(/^1 file$/);
    await userEvent.click(screen.getByRole('button', { name: 'Show every folder\'s files again' }));
    expect(onClearFocus).toHaveBeenCalledOnce();
    await userEvent.click(screen.getByRole('button', { name: /^No location/ }));
    expect(onFilter).toHaveBeenCalledExactlyOnceWith('noloc');
  });

  it('says when there is nothing to show, but not while the folder is still being read', () => {
    const { rerender, ...handlers } = show({ files: [], reading: true });
    expect(screen.queryByText('No files here')).not.toBeInTheDocument();
    // With every file showing there is nothing to clear.
    expect(screen.queryByRole('button', { name: 'Clear all' })).not.toBeInTheDocument();
    rerender(<FileGallery files={[]} places={PLACES} page={1} filter="todo" counts={{ all: 0, todo: 0, noloc: 0 }} narrowed picked={new Set()} empty="No files match “zz”" tagOf={() => undefined}
      onPage={handlers.onPage} onFilter={handlers.onFilter} onClearFocus={handlers.onClearFocus} onClearAll={handlers.onClearAll} onPick={handlers.onPick} onOpen={handlers.onOpen} onDragStart={handlers.onDragStart} onDragEnd={handlers.onDragEnd} />);
    expect(screen.getByText('No files match “zz”')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Clear all' })).toBeInTheDocument();
  });
});

describe('the overlay while a folder is being read', () => {
  const overlay = () => screen.getByRole('status', { name: 'Reading PC_backup' });

  it('shows that something is happening before the service has counted the files', () => {
    const { rerender } = render(<ReadingOverlay folder="PC_backup" />);
    expect(overlay()).toHaveTextContent('Reading dates and places in PC_backup…Looking through the folder…');
    expect(overlay()).toHaveTextContent('a large folder on an external disk can take a few minutes the first time. After that it is quick. Files appear here as they are read. Nothing is compared or moved.');
    expect(overlay().querySelector('.animate-spin')).not.toBeNull();
    expect(overlay().querySelector('.reading-sweep')).not.toBeNull();
    expect(within(overlay()).queryByRole('progressbar')).not.toBeInTheDocument();
    // Being read, but the files not yet counted: still nothing to measure against.
    rerender(<ReadingOverlay folder="PC_backup" progress={{ reading: true, total: 0, done: 0 }} />);
    expect(overlay()).toHaveTextContent('Looking through the folder…');
    expect(overlay().className).toContain('absolute');
  });

  it('shows how many of the folder\'s files have been gone through, and says so once the counting is over', () => {
    const { rerender } = render(<ReadingOverlay folder="PC_backup" progress={{ reading: true, total: 47980, done: 12340 }} />);
    const bar = within(overlay()).getByRole('progressbar', { name: 'Files read' });
    expect([bar.getAttribute('aria-valuenow'), bar.getAttribute('aria-valuemax')]).toEqual(['12340', '47980']);
    expect((bar.firstElementChild as HTMLElement).style.width).toBe('25%');
    expect(overlay()).toHaveTextContent('12,340 of 47,980 files · 25%');
    expect(overlay().querySelector('.reading-sweep')).toBeNull();

    rerender(<ReadingOverlay folder="PC_backup" progress={{ reading: true, total: 47980, done: 47979 }} />);
    expect(overlay()).toHaveTextContent('47,979 of 47,980 files · 99%');
    // The service has gone through them all and is making up its answer.
    rerender(<ReadingOverlay folder="PC_backup" progress={{ reading: false, total: 47980, done: 47980 }} />);
    expect(overlay()).toHaveTextContent('Putting the list together…');
    expect(within(overlay()).queryByRole('progressbar')).not.toBeInTheDocument();
  });

  it('becomes a strip above the files once there are files to show', () => {
    const { rerender } = render(<ReadingOverlay compact folder="PC_backup" progress={{ reading: true, total: 47980, done: 12340 }} />);
    expect(overlay()).toHaveTextContent('Still reading PC_backup…12,340 of 47,980 files · 25%Folders can be previewed once every file is in.');
    // In the flow of the page: it covers nothing.
    expect(overlay().className).not.toContain('absolute');
    const bar = within(overlay()).getByRole('progressbar', { name: 'Files read' });
    expect((bar.firstElementChild as HTMLElement).style.width).toBe('25%');
    expect(overlay().querySelector('.animate-spin')).not.toBeNull();

    rerender(<ReadingOverlay compact folder="PC_backup" progress={{ reading: false, total: 47980, done: 47980 }} />);
    expect(overlay()).toHaveTextContent('Still reading PC_backup…Putting the list together…');
    expect(overlay().querySelector('.reading-sweep')).not.toBeNull();
  });
});

describe('the selection bar', () => {
  function show(props: Partial<React.ComponentProps<typeof SelectionBar>> = {}) {
    const handlers = { onName: vi.fn(), onSelectAll: vi.fn(), onStartNaming: vi.fn(), onCancelNaming: vi.fn(), onCreate: vi.fn(), onDelete: vi.fn(), onClear: vi.fn() };
    render(<SelectionBar count={6} shown={4212} hint="" naming={false} name="" {...handlers} {...props} />);
    return handlers;
  }
  const bar = () => screen.getByRole('region', { name: 'Selection' });

  it('says how many are selected and offers to take them all, make a folder of them, or let them go', async () => {
    const { onSelectAll, onStartNaming, onClear } = show({ hint: 'or add them to one of your folders' });
    expect(bar()).toHaveTextContent('6 selectedSelect all 4,212 shownor add them to one of your foldersNew folder from theseClear');
    await userEvent.click(within(bar()).getByRole('button', { name: 'Select all 4,212 shown' }));
    await userEvent.click(within(bar()).getByRole('button', { name: 'New folder from these' }));
    await userEvent.click(within(bar()).getByRole('button', { name: 'Clear' }));
    expect([onSelectAll, onStartNaming, onClear].map(f => f.mock.calls.length)).toEqual([1, 1, 1]);
  });

  it('has a bin to delete the selected files with, as soon as one is selected', async () => {
    const { onDelete } = show({ count: 1, shown: 4212 });
    const bin = within(bar()).getByRole('button', { name: 'Delete the 1 selected file' });
    expect(bin).toHaveAttribute('title', 'Delete the selected files');
    expect(bin.querySelector('svg')).not.toBeNull();
    await userEvent.click(bin);
    expect(onDelete).toHaveBeenCalledOnce();
  });

  it('does not offer to select what is already all selected', () => {
    show({ count: 12, shown: 12 });
    expect(within(bar()).getByRole('button', { name: 'Delete the 12 selected files' })).toBeInTheDocument();
    expect(within(bar()).queryByRole('button', { name: /^Select all/ })).not.toBeInTheDocument();
  });

  it('turns into the name of the new folder, made on Enter or the button', async () => {
    const { onName, onCreate, onCancelNaming } = show({ naming: true, name: 'Trips' });
    const input = within(bar()).getByRole('textbox', { name: 'Name of the new folder' });
    expect(input).toHaveFocus();
    expect(input).toHaveValue('Trips');
    expect(input).toHaveAttribute('placeholder', 'Name the new folder, for example Trips\\Glacier 2022');
    fireEvent.change(input, { target: { value: 'Trips\\Glacier 2022' } });
    expect(onName).toHaveBeenCalledExactlyOnceWith('Trips\\Glacier 2022');
    fireEvent.keyDown(input, { key: 'a' });
    expect(onCreate).not.toHaveBeenCalled();
    fireEvent.keyDown(input, { key: 'Enter' });
    await userEvent.click(within(bar()).getByRole('button', { name: 'Create folder' }));
    expect(onCreate).toHaveBeenCalledTimes(2);
    await userEvent.click(within(bar()).getByRole('button', { name: 'Cancel' }));
    expect(onCancelNaming).toHaveBeenCalledOnce();
    expect(within(bar()).queryByRole('button', { name: 'Clear' })).not.toBeInTheDocument();
    expect(within(bar()).queryByRole('button', { name: /^Delete/ })).not.toBeInTheDocument();
  });
});

describe('the suggested folders panel', () => {
  const batch = (changes: Partial<OrganizeBatchDto> = {}): OrganizeBatchDto => ({ id: 'b1', label: 'Butte', mode: 'move', count: 13, bytes: 30_304_000, utc: '2026-10-08T18:20:35Z', ...changes });

  function show(props: Partial<React.ComponentProps<typeof SuggestionsPanel>> = {}, settings: Partial<OrganizeSettings> = {}) {
    const all = { ...defaultSettings, ...settings };
    const suggestions = suggest(LISTING, all, noChoices);
    const handlers = {
      onSettings: vi.fn(), onShowMap: vi.fn(), onFocus: vi.fn(), onPreview: vi.fn(), onDrop: vi.fn(), onGroupByDate: vi.fn(), onName: vi.fn(), onStartNaming: vi.fn(), onCancelNaming: vi.fn(),
      onCreate: vi.fn(), onAddPicked: vi.fn(), onRemoveFolder: vi.fn(), onUndo: vi.fn(),
    };
    const view = render(<SuggestionsPanel settings={all} showMap={false} suggestions={suggestions} map={mapLayout(suggestions, PLACES, all)} unplaced={0} folders={[]} picked={0} dragging={0}
      naming={false} name="" batches={[]} busy={false} reading={false} rootName="Phone Pictures" {...handlers} {...props} />);
    return { ...handlers, ...view, suggestions };
  }
  const panel = () => screen.getByRole('complementary', { name: 'Suggested folders' });
  const group = (name: string) => within(panel()).getByRole('group', { name });
  const choice = (groupName: string, name: string) => within(group(groupName)).getByRole('button', { name });
  const own = (changes: Partial<OwnFolder> = {}): OwnFolder => ({ id: 'own-1', name: 'Trips\\Glacier 2022', files: [FILES[0], FILES[1]], ...changes });

  it('says how many folders are suggested, for how many files, and how they would be named', () => {
    show();
    expect(within(panel()).getByRole('heading', { name: 'Suggested folders' }).parentElement).toHaveTextContent('Suggested folders2 · 8 files');
    const cards = within(panel()).getAllByRole('button', { name: /^Show the files for / });
    expect(cards.map(c => c.textContent)).toEqual([
      'Anaconda5 files · 29.1 MBJul 2022Inside: a folder for each yearNo landmark known here, so it is named after the town',
      'Glacier National Park3 files · 17.4 MBJul 2022Inside: a folder for each yearIncludes Apgar Village (2 mi away)',
    ]);
    // Four pictures to a card, with an empty square where the folder has fewer.
    expect(cards[0].querySelectorAll('img')).toHaveLength(3);
    expect(cards[1].querySelectorAll('img')).toHaveLength(3);
    expect(cards[1].querySelectorAll('.bg-s3:not(img)')).toHaveLength(1);
    expect(panel()).toHaveTextContent('Folders are made inside Phone Pictures. Files already in the right folder are left alone, and nothing is ever replaced: if a name is taken, (1) is added.');
  });

  it('changes how the files are grouped by place', async () => {
    const { onSettings, onShowMap } = show();
    expect([choice('Group by', 'Place'), choice('Then by', 'Year'), choice('Nearby places', 'Combine'), choice('Combine places within', '5')].map(b => b.getAttribute('aria-pressed'))).toEqual(['true', 'true', 'true', 'true']);
    expect(within(group('Then by')).getAllByRole('button').map(b => b.textContent)).toEqual(['Year', 'Month', 'Nothing']);
    expect(within(panel()).queryByRole('group', { name: 'One folder per' })).not.toBeInTheDocument();
    await userEvent.click(choice('Group by', 'Date'));
    await userEvent.click(choice('Nearby places', 'Keep separate'));
    await userEvent.click(choice('Combine places within', '50 mi'));
    await userEvent.click(choice('Then by', 'Month'));
    await userEvent.click(choice('Then by', 'Nothing'));
    expect(onSettings.mock.calls.map(c => c[0])).toEqual([{ group: 'date' }, { near: 'separate' }, { radiusMiles: 50 }, { placeThen: 'month' }, { placeThen: 'none' }]);
    await userEvent.click(within(panel()).getByRole('switch'));
    expect(onShowMap).toHaveBeenCalledExactlyOnceWith(true);
  });

  it('does not ask how far when places are kept separate', () => {
    show({}, { near: 'separate', placeThen: 'none' });
    expect(within(panel()).queryByRole('group', { name: 'Combine places within' })).not.toBeInTheDocument();
    expect(choice('Then by', 'Nothing')).toHaveAttribute('aria-pressed', 'true');
    // Not split inside, so the cards say nothing of what is inside.
    expect(panel()).not.toHaveTextContent('Inside:');
  });

  it('offers the ways of naming a folder, each with an example', async () => {
    const { onSettings } = show();
    const opener = within(panel()).getByRole('button', { name: 'Folder names' });
    expect(opener).toHaveTextContent('Landmark or area');
    await userEvent.click(opener);
    const options = within(screen.getByRole('listbox', { name: 'Folder names' })).getAllByRole('option');
    expect(options.map(o => [o.textContent, o.getAttribute('aria-selected')])).toEqual([
      ['Landmark or areaOld Faithful, Yellowstone', 'true'], ['Town, stateButte, Montana', 'false'], ['State \\ landmark or areaMontana\\Uptown Butte', 'false'],
    ]);
    await userEvent.click(options[1]);
    expect(onSettings).toHaveBeenCalledExactlyOnceWith({ nameFormat: 'city' });
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();

    // Opened again, it closes on Escape and on a second press of its button.
    await userEvent.click(opener);
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    await userEvent.click(opener);
    await userEvent.click(opener);
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  it('groups by date into a folder for each year or each month', async () => {
    const { onSettings } = show({}, { group: 'date' });
    expect(within(panel()).queryByRole('switch')).not.toBeInTheDocument();
    expect(within(panel()).queryByRole('group', { name: 'Nearby places' })).not.toBeInTheDocument();
    expect(choice('One folder per', 'Year')).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(choice('One folder per', 'Month'));
    expect(onSettings).toHaveBeenCalledExactlyOnceWith({ dateGrain: 'month' });
    expect(within(panel()).getByRole('button', { name: 'Show the files for 2022' })).toHaveTextContent(/^20229 files · 52\.3 MBJul 2022$/);
    // A year or month can be split by place inside, or by nothing.
    expect(within(group('Then by')).getAllByRole('button').map(b => [b.textContent, b.getAttribute('aria-pressed')])).toEqual([['Place', 'false'], ['Nothing', 'true']]);
    await userEvent.click(choice('Then by', 'Place'));
    expect(onSettings).toHaveBeenLastCalledWith({ dateThen: 'place' });
  });

  it('groups by date and then by place, asking how places are gathered and named as it does by place alone', async () => {
    const { onSettings } = show({ showMap: true }, { group: 'date', dateThen: 'place' });
    expect([choice('Group by', 'Date'), choice('One folder per', 'Year'), choice('Then by', 'Place'), choice('Nearby places', 'Combine'), choice('Combine places within', '5')].map(b => b.getAttribute('aria-pressed')))
      .toEqual(['true', 'true', 'true', 'true', 'true']);
    expect(within(panel()).getByRole('button', { name: 'Folder names' })).toHaveTextContent('Landmark or area');
    expect(within(panel()).getByRole('button', { name: 'Show the files for 2022' })).toHaveTextContent('20229 files · 52.3 MBJul 2022Inside: a folder for each place');
    // The map is of folders by place; these are folders by year.
    expect(within(panel()).queryByRole('switch')).not.toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Map of the suggested folders' })).not.toBeInTheDocument();
    await userEvent.click(choice('Then by', 'Nothing'));
    expect(onSettings).toHaveBeenCalledExactlyOnceWith({ dateThen: 'none' });
  });

  it('shows the folders of one that is picked, previews one, and takes files dropped on one', async () => {
    const { onFocus, onPreview, onDrop, suggestions } = show({ focus: { kind: 'suggested', key: 'not this one' } });
    const key = suggestions[0].key;
    await userEvent.click(within(panel()).getByRole('button', { name: 'Show the files for Anaconda' }));
    expect(onFocus).toHaveBeenLastCalledWith({ kind: 'suggested', key });
    await userEvent.click(within(panel()).getByRole('button', { name: 'Preview Anaconda' }));
    expect(onPreview).toHaveBeenCalledExactlyOnceWith({ kind: 'suggested', key });

    const card = within(panel()).getByRole('button', { name: 'Show the files for Anaconda' }).parentElement!;
    expect(card).toHaveClass('border-line');
    fireEvent.dragOver(card);
    expect(card).toHaveClass('border-brand', 'bg-sel');
    fireEvent.dragLeave(card);
    expect(card).toHaveClass('border-line');
    fireEvent.dragOver(card);
    fireEvent.drop(card);
    expect(card).toHaveClass('border-line');
    expect(onDrop).toHaveBeenCalledExactlyOnceWith({ kind: 'suggested', key });
  });

  it('lets go of the folder that is picked when it is clicked again', async () => {
    const { suggestions } = show();
    const key = suggestions[0].key;
    const { onFocus } = show({ focus: { kind: 'suggested', key } });
    const card = screen.getAllByRole('button', { name: 'Show the files for Anaconda' })[1];
    expect(card).toHaveAttribute('aria-pressed', 'true');
    expect(card.parentElement).toHaveClass('border-brand');
    await userEvent.click(card);
    expect(onFocus).toHaveBeenCalledExactlyOnceWith(undefined);
  });

  it('draws the folders on a map: a dot each, labelled, with the reach places were combined within', async () => {
    const { onFocus, suggestions } = show({ showMap: true });
    const map = within(panel()).getByRole('group', { name: 'Map of the suggested folders' });
    const dots = within(map).getAllByRole('button', { name: / files$/ });
    expect(dots.map(d => [d.getAttribute('aria-label'), d.getAttribute('aria-pressed')])).toEqual([['Anaconda · 5 files', 'false'], ['Glacier National Park · 3 files', 'false']]);
    // Besides the dots there is only the way to open the map large.
    expect(within(map).getAllByRole('button')).toHaveLength(3);
    expect(within(map).getByRole('button', { name: 'Enlarge the map' })).toBeInTheDocument();
    expect(map).toHaveTextContent(/^AnacondaWest Glacier20 mi$/);
    // The reach round each of the three places, and a mark for each of the two that share a folder.
    expect(map.querySelectorAll('.border-dashed')).toHaveLength(3);
    expect(map.querySelectorAll('.bg-t2')).toHaveLength(2);
    // With no key for Google's map set, the plain one is drawn, and says how to get the other.
    expect(panel()).toHaveTextContent('Places within 5 miles of each other share a folder. Larger places are labeled. Hover a dot for its name, click to see its files. Click the map itself to open it large. This is a plain map. To see streets and satellite pictures, set a Google Maps key: the README says how.');
    await userEvent.click(dots[1]);
    expect(onFocus).toHaveBeenCalledExactlyOnceWith({ kind: 'suggested', key: suggestions[1].key });
  });

  it('says a reach of one mile as one mile', () => {
    show({ showMap: true }, { radiusMiles: 1 });
    expect(panel()).toHaveTextContent('Places within 1 mile of each other share a folder.');
  });

  it('marks the picked folder on the map, and lets it go from there too', async () => {
    const towns = Array.from({ length: 8 }, (_, i) => place({ town: `T${i}`, latitude: 40 + i, longitude: i === 7 ? -90 : -100 }));
    const spread: OrganizeFilesDto = { ...LISTING, places: towns, files: towns.flatMap((_, i) => Array.from({ length: 8 - i }, (__, n) => at(`t${i}-${n}`, i))) };
    const settings = { ...defaultSettings, near: 'separate' as const };
    const suggestions = suggest(spread, settings, noChoices), last = suggestions[7];
    const onFocus = vi.fn();
    render(<SuggestionsPanel settings={settings} showMap suggestions={suggestions} map={mapLayout(suggestions, towns, settings)} focus={{ kind: 'suggested', key: last.key }} unplaced={0} folders={[]} picked={0}
      dragging={0} naming={false} name="" batches={[]} busy={false} reading={false} rootName="Phone Pictures" onSettings={vi.fn()} onShowMap={vi.fn()} onFocus={onFocus} onPreview={vi.fn()} onDrop={vi.fn()}
      onGroupByDate={vi.fn()} onName={vi.fn()} onStartNaming={vi.fn()} onCancelNaming={vi.fn()} onCreate={vi.fn()} onAddPicked={vi.fn()} onRemoveFolder={vi.fn()} onUndo={vi.fn()} />);
    const map = screen.getByRole('group', { name: 'Map of the suggested folders' });
    // The six largest are labelled, and so is the picked one though it is the smallest; its label is on the left, being far to the east.
    expect(map).toHaveTextContent('T0T1T2T3T4T5T7');
    expect(map.querySelectorAll('.border-dashed')).toHaveLength(0);
    expect(within(map).getByText('T7')).toHaveClass('border-brand');
    expect(within(map).getByText('T7').style.right).not.toBe('');
    expect(within(map).getByText('T0').style.left).not.toBe('');
    expect(screen.getByRole('complementary')).toHaveTextContent('Every place gets its own folder. Larger places are labeled.');
    await userEvent.click(within(map).getByRole('button', { name: 'T7 · 1 file' }));
    expect(onFocus).toHaveBeenCalledExactlyOnceWith(undefined);
  });

  it('shows no map when it is switched off, when grouping by date, or when there is nothing to put on it', () => {
    show({ showMap: false });
    expect(screen.queryByRole('group', { name: 'Map of the suggested folders' })).not.toBeInTheDocument();
    show({ showMap: true, map: undefined });
    show({ showMap: true }, { group: 'date' });
    expect(screen.queryByRole('group', { name: 'Map of the suggested folders' })).not.toBeInTheDocument();
  });

  it('says so when there is nothing left to suggest, but not while the folder is still being read', () => {
    const { rerender } = show({ suggestions: [] });
    expect(panel()).toHaveTextContent('Every file with a location is organized or in one of your folders.');
    expect(within(panel()).getByRole('heading', { name: 'Suggested folders' }).parentElement).toHaveTextContent(/^Suggested folders$/);
    rerender(<></>);
    show({ suggestions: [] }, { group: 'date' });
    expect(panel()).toHaveTextContent('Every file is organized or in one of your folders.');
    rerender(<></>);
  });

  it('keeps quiet about what is missing while the folder is being read', () => {
    show({ suggestions: [], reading: true, unplaced: 3 });
    expect(panel()).not.toHaveTextContent('Every file with a location');
    expect(panel()).not.toHaveTextContent('No location ·');
  });

  it('counts the files with no location, and offers to show them or to group by date instead', async () => {
    const { onFocus, onGroupByDate } = show({ unplaced: 561 });
    expect(panel()).toHaveTextContent('No location · 561 filesThese have no place in their details, so they are left out here. Group them by date instead.');
    await userEvent.click(within(panel()).getByRole('button', { name: 'Show the files with no location' }));
    expect(onFocus).toHaveBeenCalledExactlyOnceWith({ kind: 'unplaced' });
    await userEvent.click(within(panel()).getByRole('button', { name: 'Group by date' }));
    expect(onGroupByDate).toHaveBeenCalledOnce();
  });

  it('does not count files with no location when grouping by date takes them in anyway', () => {
    show({ unplaced: 561 }, { group: 'date' });
    expect(panel()).not.toHaveTextContent('No location ·');
  });

  it('explains your own folders until there is one, and names a new one in place', async () => {
    const { onStartNaming } = show();
    expect(panel()).toHaveTextContent('Make a folder of your own, then select or drag files onto it. Files you add are left out of the suggestions.');
    await userEvent.click(within(panel()).getByRole('button', { name: 'New folder' }));
    expect(onStartNaming).toHaveBeenCalledOnce();
  });

  it('takes the name of a new folder, saying what will go in it', async () => {
    const { onName, onCreate, onCancelNaming, rerender, ...rest } = show({ naming: true, name: 'Trips', picked: 6 });
    const input = within(panel()).getByRole('textbox', { name: 'Folder name' });
    expect(input).toHaveFocus();
    expect(panel()).toHaveTextContent('The 6 selected files go in it. Use \\ for subfolders.');
    expect(panel()).not.toHaveTextContent('Make a folder of your own');
    fireEvent.change(input, { target: { value: 'Trips\\Glacier' } });
    expect(onName).toHaveBeenCalledExactlyOnceWith('Trips\\Glacier');
    fireEvent.keyDown(input, { key: 'x' });
    fireEvent.keyDown(input, { key: 'Enter' });
    await userEvent.click(within(panel()).getByRole('button', { name: 'Create' }));
    expect(onCreate).toHaveBeenCalledTimes(2);
    await userEvent.click(within(panel()).getByRole('button', { name: 'Cancel' }));
    expect(onCancelNaming).toHaveBeenCalledOnce();
    expect(rest.onStartNaming).not.toHaveBeenCalled();
  });

  it('says one selected file goes in a new folder, or that files are added afterwards', () => {
    show({ naming: true, picked: 1 });
    expect(panel()).toHaveTextContent('The 1 selected file goes in it. Use \\ for subfolders.');
    show({ naming: true, picked: 0 });
    expect(screen.getAllByRole('complementary')[1]).toHaveTextContent('Use \\ for subfolders. Add files to it afterwards.');
  });

  it('lists your folders with what is in each, to add to, look into, preview or remove', async () => {
    const empty = own({ id: 'own-2', name: 'Empty one', files: [] });
    const { onFocus, onAddPicked, onPreview, onRemoveFolder, onDrop } = show({ folders: [own(), empty], picked: 3 });
    const card = within(panel()).getByRole('button', { name: 'Show the files for Trips\\Glacier 2022' });
    expect(card).toHaveTextContent('Trips\\Glacier 20222 files · 11.6 MB · not moved yet');
    expect(within(panel()).getByRole('button', { name: 'Show the files for Empty one' })).toHaveTextContent('Empty oneEmpty. Select or drag files onto it.');
    expect(within(panel()).getByRole('button', { name: 'Preview Empty one' })).toBeDisabled();
    expect(panel()).not.toHaveTextContent('Make a folder of your own');

    await userEvent.click(card);
    expect(onFocus).toHaveBeenCalledExactlyOnceWith({ kind: 'own', key: 'own-1' });
    await userEvent.click(within(panel()).getAllByRole('button', { name: 'Add 3 here' })[0]);
    expect(onAddPicked).toHaveBeenCalledExactlyOnceWith('own-1');
    await userEvent.click(within(panel()).getByRole('button', { name: 'Preview Trips\\Glacier 2022' }));
    expect(onPreview).toHaveBeenCalledExactlyOnceWith({ kind: 'own', key: 'own-1' });
    await userEvent.click(within(panel()).getByRole('button', { name: 'Remove Empty one' }));
    expect(onRemoveFolder).toHaveBeenCalledExactlyOnceWith('own-2');
    fireEvent.drop(card.parentElement!);
    expect(onDrop).toHaveBeenCalledExactlyOnceWith({ kind: 'own', key: 'own-1' });
  });

  it('offers to add to a folder only while files are selected, and lets go of the one picked', async () => {
    const { onFocus } = show({ folders: [own()], focus: { kind: 'own', key: 'own-1' } });
    expect(within(panel()).queryByRole('button', { name: /^Add / })).not.toBeInTheDocument();
    const card = within(panel()).getByRole('button', { name: 'Show the files for Trips\\Glacier 2022' });
    expect(card.parentElement).toHaveClass('border-brand');
    await userEvent.click(card);
    expect(onFocus).toHaveBeenCalledExactlyOnceWith(undefined);
  });

  it('shows where to drop while files are being dragged: each of your folders, or a new one', () => {
    const { onDrop } = show({ dragging: 3, folders: [own()] });
    const tray = within(panel()).getByRole('group', { name: 'Drop the files on a folder' });
    expect(tray).toHaveTextContent('Drop 3 files on one of your folders, or on any suggested folder belowTrips\\Glacier 2022New folder');
    const [folderPill, newPill] = [...tray.querySelectorAll<HTMLElement>('[data-drop]')];
    fireEvent.dragOver(newPill);
    expect(newPill).toHaveClass('bg-brand');
    fireEvent.dragLeave(newPill);
    expect(newPill).toHaveClass('bg-bg');
    fireEvent.drop(folderPill);
    fireEvent.drop(newPill);
    expect(onDrop.mock.calls.map(c => c[0])).toEqual([{ kind: 'own', key: 'own-1' }, 'new']);
  });

  it('has no drop tray when nothing is being dragged', () => {
    show();
    expect(within(panel()).queryByRole('group', { name: 'Drop the files on a folder' })).not.toBeInTheDocument();
  });

  it('lists what was moved or copied lately, each with the way to take it back', async () => {
    const { onUndo } = show({ batches: [batch(), batch({ id: 'b2', label: 'Trips', mode: 'copy', count: 1, bytes: 3_670_000 }), batch({ id: 'b3', label: ROOT, mode: 'remove', count: 2 })] });
    expect(within(panel()).getByRole('heading', { name: 'Recently moved' })).toBeInTheDocument();
    const rows = within(panel()).getAllByRole('listitem').map(li => li.textContent);
    expect(rows[0]).toMatch(/^13 files moved to Butte28\.9 MB · \d{1,2}:\d{2}.*Undo$/);
    expect(rows[1]).toMatch(/^1 file copied to Trips3\.5 MB · /);
    // What was deleted is listed by the folder it was taken out of, and can be put back the same way.
    expect(rows[2]).toMatch(/^2 files deleted from Phone Pictures28\.9 MB · /);
    await userEvent.click(within(panel()).getByRole('button', { name: 'Undo Trips' }));
    expect(onUndo).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ id: 'b2', label: 'Trips', mode: 'copy' }));
    await userEvent.click(within(panel()).getByRole('button', { name: 'Undo deleting from Phone Pictures' }));
    expect(onUndo).toHaveBeenLastCalledWith(expect.objectContaining({ id: 'b3', mode: 'remove' }));
  });

  it('lets nothing be undone while a move is under way, and has no such list when nothing was moved', () => {
    show({ batches: [batch()], busy: true });
    expect(within(panel()).getByRole('button', { name: 'Undo Butte' })).toBeDisabled();
    show();
    expect(within(screen.getAllByRole('complementary')[1]).queryByRole('heading', { name: 'Recently moved' })).not.toBeInTheDocument();
  });
});
