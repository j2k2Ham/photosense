import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { DuplicateStrip } from '../../components/DuplicateStrip';
import { member } from '../fixtures';

const identical = member({ id: 'm1', fileName: 'IMG_4198 (1).JPG', folder: 'D:\\backup\\2024', takenOn: '2024-04-07T17:35:59', placeName: 'Buxton, North Carolina, US' }, 'identical', 'Same quality; this one is in the primary folder');
const converted = member({ id: 'm2', fileName: 'IMG_4198.HEIC', format: 'HEIC', fileSizeBytes: 3_046_000 }, 'samePicture', 'Opens everywhere: JPEG rather than HEIC');
const burst = member({ id: 'm3', fileName: 'IMG_4199.JPG' }, 'similar');
const keeping = member({ id: 'm4', fileName: 'IMG_4198 (2).JPG', kept: true }, 'identical');

const tile = (name: string) => screen.getByRole('button', { name: `Open ${name}` });

describe('DuplicateStrip', () => {
  it('shows a tile for each match, labelled with how sure the match is', () => {
    render(<DuplicateStrip members={[identical, converted, burst, keeping]} onOpen={() => undefined} />);

    expect(tile('IMG_4198 (1).JPG')).toHaveTextContent('IDENTICAL FILE');
    expect(tile('IMG_4198.HEIC')).toHaveTextContent('SAME PICTURE');
    expect(tile('IMG_4199.JPG')).toHaveTextContent('SIMILAR');
    expect(within(tile('IMG_4199.JPG')).getByText('SIMILAR')).toHaveClass('bg-amber-500/80');
    expect(within(tile('IMG_4198.HEIC')).getByText('SAME PICTURE')).toHaveClass('bg-neutral-900/75');
  });

  it('marks a copy that is being kept', () => {
    render(<DuplicateStrip members={[identical, keeping]} onOpen={() => undefined} />);
    expect(tile('IMG_4198 (2).JPG')).toHaveTextContent('KEEPING');
    expect(tile('IMG_4198 (2).JPG')).toHaveClass('border-emerald-500');
    expect(tile('IMG_4198 (1).JPG')).toHaveClass('border-transparent');
  });

  it('describes the copy under the pointer, and why the original was preferred to it', () => {
    render(<DuplicateStrip members={[identical, converted]} onOpen={() => undefined} />);
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

  it('says of a similar shot that it is not a copy', () => {
    render(<DuplicateStrip members={[burst]} onOpen={() => undefined} />);
    fireEvent.mouseEnter(tile('IMG_4199.JPG'), { clientX: 100, clientY: 100 });
    expect(screen.getByRole('tooltip')).toHaveTextContent('A different shot or an edited version. Not removed in bulk.');
    expect(screen.getByRole('tooltip')).not.toHaveTextContent('Original preferred');
  });

  it('keeps the description beside the pointer and inside the window', () => {
    // The test window is 1024 by 768.
    render(<DuplicateStrip members={[identical]} onOpen={() => undefined} />);

    fireEvent.mouseEnter(tile('IMG_4198 (1).JPG'), { clientX: 100, clientY: 100 });
    expect(screen.getByRole('tooltip')).toHaveStyle({ left: '116px', top: '116px', width: '320px' });
    expect(screen.getByRole('tooltip').style.bottom).toBe('');

    // Near the right and bottom edges it is held in, and opens upwards.
    fireEvent.mouseMove(tile('IMG_4198 (1).JPG'), { clientX: 1000, clientY: 700 });
    expect(screen.getByRole('tooltip')).toHaveStyle({ left: '692px', bottom: '80px' });
    expect(screen.getByRole('tooltip').style.top).toBe('');
  });

  it('opens the copy that is clicked and puts the description away', () => {
    const onOpen = vi.fn();
    render(<DuplicateStrip members={[identical, converted]} onOpen={onOpen} />);
    fireEvent.mouseEnter(tile('IMG_4198.HEIC'), { clientX: 100, clientY: 100 });

    fireEvent.click(tile('IMG_4198.HEIC'));

    expect(onOpen).toHaveBeenCalledExactlyOnceWith(converted);
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
  });
});
