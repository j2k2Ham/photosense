import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { Differences, DiffTable } from '../../components/Differences';
import { DuplicateStrip } from '../../components/DuplicateStrip';
import { GroupList } from '../../components/GroupList';
import { PhotoStill, PhotoThumb, PhotoView } from '../../components/PhotoThumb';
import { ReviewPanel } from '../../components/ReviewPanel';
import { imageUrl, thumbnailUrl, videoUrl } from '../../lib/apiClient';
import type { DuplicateGroupDto, GroupMode } from '../../types';
import { group, member, photo, video } from '../fixtures';

const original = photo({ id: 'p1', fileName: 'IMG_4198.JPG', folder: 'C:\\photos\\2024', takenOn: '2024-04-07T17:35:59' });
const identical = member({ id: 'm1', fileName: 'IMG_4198 (1).JPG', folder: 'D:\\backup\\2024', takenOn: '2024-04-07T17:35:59', placeName: 'Buxton, North Carolina, US' }, 'identical', 'Same quality; this one is in the primary folder');
const converted = member({ id: 'm2', fileName: 'IMG_4198.HEIC', folder: 'C:\\photos\\2024', takenOn: '2024-04-07T17:35:59', format: 'HEIC', fileSizeBytes: 3_046_000 }, 'samePicture', 'Opens everywhere: JPEG rather than HEIC');
const burst = member({ id: 'm3', fileName: 'IMG_4199.JPG' }, 'similar');
const keeping = member({ id: 'm4', fileName: 'IMG_4198 (2).JPG', kept: true }, 'identical');

describe('PhotoThumb', () => {
  it('shows a picture by its cached preview, fetched at once so that none appears late', () => {
    const { container } = render(<PhotoThumb photo={photo()} className="h-14 w-14" />);
    const img = container.querySelector('img')!;
    expect(img).toHaveAttribute('src', thumbnailUrl('p1'));
    expect(img).not.toHaveAttribute('loading');
    expect(img).toHaveAttribute('decoding', 'async');
    expect(img).toHaveClass('object-cover', 'h-14');
  });

  it('shows a video as a dark tile with its playing time, when that is known', () => {
    expect(render(<PhotoThumb photo={video()} />).container).toHaveTextContent(/^1:24$/);
    expect(render(<PhotoThumb photo={video({ durationSeconds: undefined })} />).container).toHaveTextContent(/^$/);
    expect(render(<PhotoThumb photo={video({ durationSeconds: 0 })} />).container).toHaveTextContent(/^0:00$/);
  });
});

describe('PhotoStill and PhotoView', () => {
  it('show a picture at its true shape over its preview', () => {
    const { container } = render(<PhotoStill photo={photo()} />);
    expect(screen.getByRole('img', { name: 'IMG_4198.JPG' })).toHaveAttribute('src', imageUrl('p1'));
    expect(container.firstElementChild).toHaveStyle({ backgroundImage: `url("${thumbnailUrl('p1')}")` });
    expect(render(<PhotoView photo={photo()} onOpenInViewer={() => undefined} />).container.querySelector('img')).not.toBeNull();
  });

  it('show a video as a still tile on the desk and as a player in the window', async () => {
    expect(render(<PhotoStill photo={video()} />).container.querySelector('img, video')).toBeNull();

    const onOpenInViewer = vi.fn();
    const clip = video();
    const { container } = render(<PhotoView photo={clip} onOpenInViewer={onOpenInViewer} />);
    const player = container.querySelector('video')!;
    expect(player).toHaveAttribute('src', videoUrl('v1'));
    expect(player).toHaveAttribute('controls');
    await userEvent.click(screen.getByRole('button', { name: 'Open in default player' }));
    expect(onOpenInViewer).toHaveBeenCalledExactlyOnceWith(clip);
  });

  it('keep playing a video the browser can decode', () => {
    const { container } = render(<PhotoView photo={video()} onOpenInViewer={() => undefined} />);
    const player = container.querySelector('video')!;
    Object.defineProperty(player, 'videoWidth', { value: 1920 });
    fireEvent.loadedMetadata(player);
    expect(container.querySelector('video')).not.toBeNull();
  });

  it.each([
    ['fails to load', (player: HTMLVideoElement) => fireEvent.error(player)],
    ['loads without a picture', (player: HTMLVideoElement) => fireEvent.loadedMetadata(player)],
  ])('say so when a video %s, and try again with the next one', (_what, happen) => {
    const { container, rerender } = render(<PhotoView photo={video()} onOpenInViewer={() => undefined} />);
    happen(container.querySelector('video')!);
    expect(container.querySelector('video')).toBeNull();
    expect(screen.getByText('This browser cannot play this video. Open it in your default player instead.')).toBeInTheDocument();

    rerender(<PhotoView photo={video({ id: 'v2' })} onOpenInViewer={() => undefined} />);
    expect(container.querySelector('video')).toHaveAttribute('src', videoUrl('v2'));
  });
});

describe('Differences', () => {
  it('lists what sets the copy apart, each under its name', () => {
    render(<Differences original={original} copy={converted.photo} />);
    expect(screen.getByText('Differs from the original in')).toBeInTheDocument();
    expect(screen.getAllByRole('listitem').map(li => li.textContent)).toEqual(['Name: the original is IMG_4198.JPG', 'Format: HEIC, the original is JPEG', 'File size: 2.9 MB, the original is 5.8 MB']);
  });

  it('says so when nothing on record sets it apart', () => {
    render(<Differences original={original} copy={original} />);
    expect(screen.getByText('Nothing that is recorded about it.')).toBeInTheDocument();
    expect(screen.queryByRole('list')).not.toBeInTheDocument();
  });
});

describe('DiffTable', () => {
  it('puts the rows that differ first and marks them, and says Same for the rest', () => {
    render(<DiffTable original={photo({ ...original, latitude: 35.2677, longitude: -75.5424 })} copy={photo({ ...converted.photo, folder: 'D:\\backup', latitude: 35.2677, longitude: -75.5424 })} />);
    const rows = within(screen.getByRole('table', { name: 'What differs' })).getAllByRole('row').slice(1);
    expect(rows.map(r => within(r).getByRole('rowheader').textContent)).toEqual(['Name', 'Format', 'File size', 'Folder', 'Taken', 'Place', 'Pixel size', 'Camera']);
    expect(rows.map(r => r.dataset.differs)).toEqual(['true', 'true', 'true', 'true', 'false', 'false', 'false', 'false']);

    const cells = (r: HTMLElement) => within(r).getAllByRole('cell').map(c => c.textContent);
    expect(cells(rows[1])).toEqual(['JPEG', 'HEIC']);
    expect(cells(rows[4])[1]).toBe('Same');
    expect(within(rows[3]).getAllByRole('cell')[1]).toHaveClass('font-mono');
    expect(within(rows[0]).getAllByRole('cell')[1]).not.toHaveClass('font-mono');
    expect(within(rows[5]).getByRole('link', { name: 'Show on map' })).toHaveAttribute('href', expect.stringContaining('openstreetmap.org'));
  });
});

describe('DuplicateStrip', () => {
  const show = (members = [identical, converted, burst, keeping], selectedIndex = 0) => {
    const onSelect = vi.fn();
    render(<DuplicateStrip original={original} members={members} selectedIndex={selectedIndex} onSelect={onSelect} />);
    return onSelect;
  };
  const tile = (name: string) => screen.getByRole('button', { name: `Show ${name}` });

  it('shows a tile for each copy with its format, how it matched, and which is beside the original', () => {
    show([identical, converted, burst, keeping, member({ id: 'm5', fileName: 'mystery.dat', format: undefined })], 1);
    expect(tile('IMG_4198 (1).JPG')).toHaveTextContent('JPEGIdentical file');
    expect(tile('IMG_4198.HEIC')).toHaveTextContent('HEICSame picture');
    expect(tile('IMG_4199.JPG')).toHaveTextContent('Similar');
    expect(tile('IMG_4198 (2).JPG')).toHaveTextContent('Keeping');
    expect(tile('mystery.dat')).toHaveTextContent(/^Identical file$/);
    expect(tile('IMG_4198.HEIC')).toHaveAttribute('aria-pressed', 'true');
    expect(tile('IMG_4198 (1).JPG')).toHaveAttribute('aria-pressed', 'false');
  });

  it('describes the copy under the pointer: what differs, and why the original was preferred', () => {
    show();
    fireEvent.mouseEnter(tile('IMG_4198.HEIC'));
    const card = screen.getByRole('tooltip');
    expect(card.parentElement).toBe(document.body);
    expect(card).toHaveTextContent('IMG_4198.HEICSame picture');
    expect(within(card).getByText('Folder').nextElementSibling).toHaveTextContent('C:\\photos\\2024');
    expect(within(card).getAllByRole('listitem').map(li => li.textContent)).toEqual(['Name: the original is IMG_4198.JPG', 'Format: HEIC, the original is JPEG', 'File size: 2.9 MB, the original is 5.8 MB']);
    expect(card).toHaveTextContent('Original preferred: Opens everywhere: JPEG rather than HEIC');

    fireEvent.mouseLeave(tile('IMG_4198.HEIC'));
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
  });

  it('says of a similar shot that it is not a copy, and of a kept one that it is kept', () => {
    show();
    fireEvent.mouseEnter(tile('IMG_4199.JPG'));
    expect(screen.getByRole('tooltip')).toHaveTextContent('A different shot or an edited version. Never removed in bulk.');
    fireEvent.mouseEnter(tile('IMG_4198 (2).JPG'));
    expect(screen.getByRole('tooltip')).toHaveTextContent('IMG_4198 (2).JPGKeeping');
  });

  it('places the description above the tile and inside the window', () => {
    // The test window is 1024 by 768.
    show();
    const at = (left: number, top: number) => vi.spyOn(tile('IMG_4198.HEIC'), 'getBoundingClientRect').mockReturnValue({ left, top } as DOMRect);
    at(300, 500);
    fireEvent.mouseEnter(tile('IMG_4198.HEIC'));
    expect(screen.getByRole('tooltip')).toHaveStyle({ left: '300px', bottom: '278px', width: '420px' });
    at(900, 500);
    fireEvent.mouseEnter(tile('IMG_4198.HEIC'));
    expect(screen.getByRole('tooltip')).toHaveStyle({ left: '592px' });
    at(2, 500);
    fireEvent.mouseEnter(tile('IMG_4198.HEIC'));
    expect(screen.getByRole('tooltip')).toHaveStyle({ left: '12px' });
  });

  it('shows the clicked copy beside the original and puts the description away', () => {
    const onSelect = show();
    fireEvent.mouseEnter(tile('IMG_4198.HEIC'));
    fireEvent.click(tile('IMG_4198.HEIC'));
    expect(onSelect).toHaveBeenCalledExactlyOnceWith(1);
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
  });
});

describe('GroupList', () => {
  const show = (props: Partial<React.ComponentProps<typeof GroupList>> = {}) => {
    const onSelect = vi.fn(), onPage = vi.fn();
    render(<GroupList groups={[]} mode="duplicates" total={0} page={1} totalPages={1} query="" onSelect={onSelect} onPage={onPage} {...props} />);
    return { onSelect, onPage };
  };

  it.each([
    ['duplicates', '', 'No duplicates to show'], ['similar', '', 'No similar shots found'], ['duplicates', ' beach ', 'No groups match "beach"'],
  ] as const)('says so when there are no %s (searching for "%s")', (mode, query, message) => {
    show({ mode, query });
    expect(screen.getByText(message)).toBeInTheDocument();
    expect(screen.getByLabelText('Groups')).toHaveTextContent('0 groups');
  });

  it('starts each page at its top, wherever the one before it was left', () => {
    const turned = (page: number) => <GroupList groups={[group()]} mode="duplicates" total={120} page={page} totalPages={3} query="" onSelect={() => undefined} onPage={() => undefined} />;
    const { rerender } = render(turned(1));
    const list = screen.getByRole('list');
    list.scrollTop = 640;
    rerender(turned(1));
    expect(list.scrollTop).toBe(640);
    rerender(turned(2));
    expect(list.scrollTop).toBe(0);
  });

  it('says it is loading until the first answer arrives', () => {
    show({ total: undefined });
    expect(screen.getByText('Loading…')).toBeInTheDocument();
    expect(screen.queryByText('No duplicates to show')).not.toBeInTheDocument();
  });

  it('shows each group by its best copy, with how many copies it has and what there is to remove', async () => {
    const groups = [
      group({ members: [identical, converted] }),
      group({ key: 'g2', keeper: video({ id: 'v1', fileName: 'DLWL2718.MP4' }), members: [member({ id: 'v2', isVideo: true })] }),
      group({ key: 'g3', keeper: photo({ id: 'p3', fileName: 'IMG_0712.JPG' }), members: [keeping] }),
    ];
    const { onSelect } = show({ groups, total: 1, selectedKey: 'g2' });
    expect(screen.getByLabelText('Groups')).toHaveTextContent('1 group');
    const [first, second, third] = screen.getAllByRole('button');
    expect(first).toHaveTextContent('+2IMG_4198.JPG2 duplicates · 5.8 MB');
    expect(second).toHaveTextContent('VIDEO+1DLWL2718.MP41 duplicate · 5.8 MB');
    expect(second).toHaveAttribute('aria-pressed', 'true');
    expect(first).toHaveAttribute('aria-pressed', 'false');
    expect(within(third).getByText('1 copy, all marked keep')).toHaveClass('text-keep');
    expect(within(first).getByText('2 duplicates · 5.8 MB')).toHaveClass('text-t2');

    await userEvent.click(first);
    expect(onSelect).toHaveBeenCalledExactlyOnceWith('g1');
  });

  it('pages through the groups, fifty at a time', async () => {
    const { onPage } = show({ groups: [group()], total: 263, page: 2, totalPages: 6 });
    expect(screen.getByLabelText('Groups')).toHaveTextContent('263 groups');
    expect(screen.getByText('Page 2 of 6')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Next' }));
    await userEvent.click(screen.getByRole('button', { name: 'Prev' }));
    expect(onPage.mock.calls).toEqual([[3], [1]]);
  });

  it('stops at the first page and the last, and shows no pager for a single page', () => {
    const first = render(<GroupList groups={[group()]} mode="duplicates" total={60} page={1} totalPages={2} query="" onSelect={() => undefined} onPage={() => undefined} />);
    expect(screen.getByRole('button', { name: 'Prev' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Next' })).toBeEnabled();
    first.unmount();
    const last = render(<GroupList groups={[group()]} mode="duplicates" total={60} page={2} totalPages={2} query="" onSelect={() => undefined} onPage={() => undefined} />);
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled();
    last.unmount();
    show({ groups: [group()], total: 1 });
    expect(screen.queryByRole('button', { name: 'Next' })).not.toBeInTheDocument();
  });
});

describe('ReviewPanel', () => {
  function show(shown: DuplicateGroupDto | undefined, options: { mode?: GroupMode; busy?: boolean; copyIndex?: number } = {}) {
    const handlers = { onSelectCopy: vi.fn(), onCompare: vi.fn(), onToggleKeep: vi.fn(), onDeleteCopy: vi.fn(), onDeleteGroup: vi.fn(), onOpenInViewer: vi.fn() };
    const view = render(<ReviewPanel group={shown} mode={options.mode ?? 'duplicates'} busy={options.busy ?? false} copyIndex={options.copyIndex ?? 0} {...handlers} />);
    return { ...handlers, ...view };
  }
  const desk = () => screen.getByLabelText('Review');
  const button = (name: string | RegExp) => within(desk()).getByRole('button', { name });

  it('asks for a group when none is chosen', () => {
    show(undefined);
    expect(screen.getByText('Select a group to review')).toBeInTheDocument();
  });

  it('shows the original beside its one copy, what differs, and one way to delete it', async () => {
    const one = group({ keeper: original, members: [converted], reclaimableBytes: 3_046_000 });
    const { onCompare, onDeleteGroup, onToggleKeep } = show(one);
    expect(desk()).toHaveTextContent('IMG_4198.JPG1 duplicate · 2.9 MB to free');
    expect(desk()).toHaveTextContent('OriginalThe best copy of this picture. It stays.');
    expect(desk()).toHaveTextContent('Same pictureCopy 1 of 1 · HEICClick either to compare');
    // Each file under its picture, by name and by folder: the copy here sits beside the original.
    expect(desk().querySelector('[data-file="original"]')).toHaveTextContent(/^IMG_4198\.JPGC:\\photos\\2024$/);
    expect(desk().querySelector('[data-file="original"] p:last-child')).toHaveClass('font-mono');
    expect(desk().querySelector('[data-file="copy"]')).toHaveTextContent(/^IMG_4198\.HEICIn the same folder as the original$/);
    expect(desk().querySelector('[data-file="copy"] p:last-child')).not.toHaveClass('font-mono');
    expect(screen.getByRole('heading', { name: 'Duplicates of this picture (1)' })).toBeInTheDocument();
    expect(desk()).toHaveTextContent('JPEG · 5.8 MB');
    expect(desk()).toHaveTextContent('HEIC · 2.9 MB');
    expect(desk()).toHaveTextContent('Original preferred: Opens everywhere: JPEG rather than HEIC');
    expect(screen.getByRole('table', { name: 'What differs' })).toBeInTheDocument();
    expect(within(desk()).queryByRole('button', { name: /^Delete this copy/ })).not.toBeInTheDocument();

    // The details scroll; what can be done with the group sits beneath them and does not.
    expect(screen.getByRole('table', { name: 'What differs' }).closest('.overflow-y-auto')).not.toBeNull();
    expect(button('Delete this duplicate · 2.9 MB').closest('.overflow-y-auto')).toBeNull();

    await userEvent.click(button('Compare IMG_4198.JPG'));
    await userEvent.click(button('Compare IMG_4198.HEIC'));
    expect(onCompare).toHaveBeenCalledTimes(2);
    await userEvent.click(button('Delete this duplicate · 2.9 MB'));
    expect(onDeleteGroup).toHaveBeenCalledExactlyOnceWith(one);
    await userEvent.click(button('Keep this copy'));
    expect(onToggleKeep).toHaveBeenCalledExactlyOnceWith(converted.photo);
  });

  it('with several copies, shows the chosen one and offers to delete it or all of them', async () => {
    const several = group({ keeper: original, members: [identical, converted, keeping], reclaimableBytes: 9_139_000 });
    const { onDeleteCopy, onDeleteGroup, onSelectCopy } = show(several, { copyIndex: 1 });
    expect(desk()).toHaveTextContent('Same pictureCopy 2 of 3 · HEIC');
    // Every copy is named under its tile, without hovering.
    expect(desk()).toHaveTextContent('Identical fileIMG_4198 (1).JPG');
    expect(desk()).toHaveTextContent('KeepingIMG_4198 (2).JPG');
    await userEvent.click(button('Delete this copy · 2.9 MB'));
    expect(onDeleteCopy).toHaveBeenCalledExactlyOnceWith(converted);
    await userEvent.click(button('Delete these 2 duplicates · 8.7 MB'));
    expect(onDeleteGroup).toHaveBeenCalledExactlyOnceWith(several);
    await userEvent.click(button('Show IMG_4198 (2).JPG'));
    expect(onSelectCopy).toHaveBeenCalledExactlyOnceWith(2);
  });

  it('says where a copy is when it is not beside the original', () => {
    show(group({ keeper: original, members: [identical] }));
    expect(desk().querySelector('[data-file="copy"]')).toHaveTextContent(/^IMG_4198 \(1\)\.JPGD:\\backup\\2024$/);
    expect(desk().querySelector('[data-file="copy"] p:last-child')).toHaveClass('font-mono');
  });

  it('will not delete a copy that is marked keep, and shows the last copy when the chosen one is gone', () => {
    show(group({ keeper: original, members: [identical, keeping] }), { copyIndex: 5 });
    expect(desk()).toHaveTextContent('KeepingCopy 2 of 2 · JPEG');
    expect(button(/^Delete this copy/)).toBeDisabled();
    expect(button('Stop keeping this copy')).toHaveClass('bg-keep-bg');
    expect(button('Delete this duplicate · 5.8 MB')).toBeEnabled();
  });

  it('offers no group deletion when every copy is kept, and nothing while something is under way', () => {
    const { unmount } = show(group({ keeper: original, members: [keeping] }));
    expect(desk()).toHaveTextContent('All copies marked keep');
    expect(within(desk()).queryByRole('button', { name: /^Delete/ })).not.toBeInTheDocument();
    unmount();
    show(group({ keeper: original, members: [identical, converted] }), { busy: true });
    for (const name of [/^Delete this copy/, /^Delete these 2/, 'Keep this copy']) expect(button(name)).toBeDisabled();
  });

  it('presents similar shots as shots to review one at a time', async () => {
    const { onDeleteCopy } = show(group({ keeper: original, members: [burst] }), { mode: 'similar' });
    expect(desk()).toHaveTextContent('IMG_4198.JPG1 similar shot');
    expect(desk()).toHaveTextContent('BestThe best of these similar shots.');
    expect(desk().querySelector('[data-file="copy"]')).toHaveTextContent(/^IMG_4199\.JPGIn the same folder as the best shot$/);
    expect(screen.getByRole('heading', { name: 'Similar shots (1)' })).toBeInTheDocument();
    expect(desk()).toHaveTextContent('A burst frame or an edited version. Never removed in bulk.');
    expect(within(desk()).queryByRole('button', { name: /keep/i })).not.toBeInTheDocument();
    expect(within(desk()).queryByRole('button', { name: /duplicates?( ·|$)/ })).not.toBeInTheDocument();
    expect(button(/^Delete this copy/)).toHaveClass('pill-rose');
    await userEvent.click(button(/^Delete this copy/));
    expect(onDeleteCopy).toHaveBeenCalledExactlyOnceWith(burst);
  });

  it('shows a video as a still, with the way to the system\'s player', async () => {
    const clip = video();
    const { onOpenInViewer } = show(group({ keeper: clip, members: [member({ id: 'v2', fileName: 'copy.MOV', isVideo: true, format: undefined })] }));
    expect(desk()).toHaveTextContent('The best copy of this video. It stays.');
    expect(screen.getByRole('heading', { name: 'Duplicates of this video (1)' })).toBeInTheDocument();
    expect(desk()).toHaveTextContent('Copy 1 of 1 · ?');
    const players = within(desk()).getAllByRole('button', { name: 'Open in default player' });
    await userEvent.click(players[0]);
    expect(onOpenInViewer).toHaveBeenCalledExactlyOnceWith(clip);
  });
});
