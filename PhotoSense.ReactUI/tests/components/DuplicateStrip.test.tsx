import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { DuplicateStrip } from '../../components/DuplicateStrip';
import type { GroupMemberDto } from '../../types';
import { member, photo } from '../fixtures';

const original = photo({ id: 'p1', fileName: 'IMG_4198.JPG', folder: 'C:\\photos\\2024', takenOn: '2024-04-07T17:35:59' });
const identical = member({ id: 'm1', fileName: 'IMG_4198 (1).JPG', folder: 'D:\\backup\\2024', takenOn: '2024-04-07T17:35:59', placeName: 'Buxton, North Carolina, US' }, 'identical', 'Same quality; this one is in the primary folder');
// What a phone import leaves: the HEIC it shot beside the JPEG made from it, same name, same folder, same moment.
const converted = member({ id: 'm2', fileName: 'IMG_4198.HEIC', folder: 'C:\\photos\\2024', takenOn: '2024-04-07T17:35:59', format: 'HEIC', fileSizeBytes: 3_046_000 }, 'samePicture', 'Opens everywhere: JPEG rather than HEIC');
const burst = member({ id: 'm3', fileName: 'IMG_4199.JPG' }, 'similar');
const keeping = member({ id: 'm4', fileName: 'IMG_4198 (2).JPG', kept: true }, 'identical');

const show = (members: GroupMemberDto[], onOpen: (m: GroupMemberDto) => void = () => undefined) => render(<DuplicateStrip original={original} members={members} onOpen={onOpen} />);
const tile = (name: string) => screen.getByRole('button', { name: `Open ${name}` });

describe('DuplicateStrip', () => {
  it('shows a tile for each match, labelled with how sure the match is', () => {
    show([identical, converted, burst, keeping]);

    expect(tile('IMG_4198 (1).JPG')).toHaveTextContent('IDENTICAL FILE');
    expect(tile('IMG_4198.HEIC')).toHaveTextContent('SAME PICTURE');
    expect(tile('IMG_4199.JPG')).toHaveTextContent('SIMILAR');
    expect(within(tile('IMG_4199.JPG')).getByText('SIMILAR')).toHaveClass('bg-amber-500/80');
    expect(within(tile('IMG_4198.HEIC')).getByText('SAME PICTURE')).toHaveClass('bg-neutral-900/75');
  });

  it('shows each tile\'s format, so a HEIC is not mistaken for the JPEG it sits beside', () => {
    show([identical, converted, member({ id: 'm5', fileName: 'mystery.dat', format: undefined })]);
    expect(within(tile('IMG_4198.HEIC')).getByText('HEIC')).toBeInTheDocument();
    expect(within(tile('IMG_4198 (1).JPG')).getByText('JPEG')).toBeInTheDocument();
    expect(tile('mystery.dat')).toHaveTextContent(/^IDENTICAL FILE$/);
  });

  it('marks a copy that is being kept', () => {
    show([identical, keeping]);
    expect(tile('IMG_4198 (2).JPG')).toHaveTextContent('KEEPING');
    expect(tile('IMG_4198 (2).JPG')).toHaveClass('border-emerald-500');
    expect(tile('IMG_4198 (1).JPG')).toHaveClass('border-transparent');
  });

  it('describes the copy under the pointer, and why the original was preferred to it', () => {
    show([identical, converted]);
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();

    fireEvent.mouseEnter(tile('IMG_4198 (1).JPG'), { clientX: 100, clientY: 100 });

    const card = screen.getByRole('tooltip');
    expect(card.parentElement).toBe(document.body);            // outside the panel, so it is placed against the window
    expect(card).toHaveTextContent('IMG_4198 (1).JPG');
    expect(within(card).getByText('Date').nextElementSibling).toHaveTextContent('2024');
    expect(within(card).getByText('Folder').nextElementSibling).toHaveTextContent('D:\\backup\\2024');
    expect(within(card).getByText('Taken at').nextElementSibling).toHaveTextContent('Buxton, North Carolina, US');
    expect(within(card).getByText('File').nextElementSibling).toHaveTextContent('4032 × 3024 · JPEG · 5.8 MB');
    expect(card).toHaveTextContent('Original preferred: Same quality; this one is in the primary folder');

    fireEvent.mouseLeave(tile('IMG_4198 (1).JPG'));
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
  });

  it('spells out how the copy differs from the original it is shown beside', () => {
    show([identical, converted]);

    // Same name but for its ending, same folder, same moment: only the list of differences tells them apart.
    fireEvent.mouseEnter(tile('IMG_4198.HEIC'), { clientX: 100, clientY: 100 });
    const differs = within(screen.getByRole('tooltip')).getAllByRole('listitem').map(li => li.textContent);
    expect(differs).toEqual(['Name: the original is IMG_4198.JPG', 'Format: HEIC, the original is JPEG', 'File size: 2.9 MB, the original is 5.8 MB']);

    fireEvent.mouseEnter(tile('IMG_4198 (1).JPG'), { clientX: 100, clientY: 100 });
    const moved = within(screen.getByRole('tooltip')).getAllByRole('listitem').map(li => li.textContent);
    expect(moved).toEqual(['Name: the original is IMG_4198.JPG', 'Folder: the original is in C:\\photos\\2024']);
  });

  it('says of a similar shot that it is not a copy', () => {
    show([burst]);
    fireEvent.mouseEnter(tile('IMG_4199.JPG'), { clientX: 100, clientY: 100 });
    expect(screen.getByRole('tooltip')).toHaveTextContent('A different shot or an edited version. Not removed in bulk.');
    expect(screen.getByRole('tooltip')).not.toHaveTextContent('Original preferred');
  });

  it('keeps the description beside the pointer and inside the window', () => {
    // The test window is 1024 by 768.
    show([identical]);

    fireEvent.mouseEnter(tile('IMG_4198 (1).JPG'), { clientX: 100, clientY: 100 });
    expect(screen.getByRole('tooltip')).toHaveStyle({ left: '116px', top: '116px', width: '320px' });
    expect(screen.getByRole('tooltip').style.bottom).toBe('');

    // Near the right and bottom edges it is held in, and opens upwards.
    fireEvent.mouseMove(tile('IMG_4198 (1).JPG'), { clientX: 1000, clientY: 700 });
    expect(screen.getByRole('tooltip')).toHaveStyle({ left: '692px', bottom: '80px' });
    expect(screen.getByRole('tooltip').style.top).toBe('');

    // Half-way down there is still room below.
    fireEvent.mouseMove(tile('IMG_4198 (1).JPG'), { clientX: 100, clientY: 440 });
    expect(screen.getByRole('tooltip')).toHaveStyle({ top: '456px' });
  });

  it('opens the copy that is clicked and puts the description away', () => {
    const onOpen = vi.fn();
    show([identical, converted], onOpen);
    fireEvent.mouseEnter(tile('IMG_4198.HEIC'), { clientX: 100, clientY: 100 });

    fireEvent.click(tile('IMG_4198.HEIC'));

    expect(onOpen).toHaveBeenCalledExactlyOnceWith(converted);
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
  });
});
