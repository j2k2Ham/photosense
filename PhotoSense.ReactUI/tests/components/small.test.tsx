import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { act, fireEvent, render, renderHook, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import RootLayout, { metadata } from '../../app/layout';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { Differences } from '../../components/Differences';
import { Footer } from '../../components/Footer';
import { GroupList } from '../../components/GroupList';
import { LogsPanel } from '../../components/LogsPanel';
import { PhotoDetails } from '../../components/PhotoDetails';
import { ProgressPanel } from '../../components/ProgressPanel';
import { Toaster, useToasts } from '../../components/Toaster';
import { TopBar } from '../../components/TopBar';
import { thumbnailUrl } from '../../lib/apiClient';
import type { ScanProgressSnapshotDto } from '../../types';
import { group, member, photo } from '../fixtures';

describe('the page frame', () => {
  it('has the product name and a link to the source', () => {
    render(<TopBar />);
    expect(screen.getByRole('heading', { name: 'PhotoSense' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'GitHub' })).toHaveAttribute('href', 'https://github.com/j2k2Ham/photosense');
  });

  it('is signed with the current year', () => {
    render(<Footer />);
    expect(screen.getByText(`PhotoSense © ${new Date().getFullYear()}`)).toBeInTheDocument();
  });

  it('puts the page between the top bar and the footer', () => {
    const html = renderToStaticMarkup(<RootLayout><p>the page itself</p></RootLayout>);
    expect(html).toMatch(/^<html lang="en" class="dark">/);
    expect(html.indexOf('<header')).toBeLessThan(html.indexOf('the page itself'));
    expect(html.indexOf('the page itself')).toBeLessThan(html.indexOf('<footer'));
    expect(metadata.title).toBe('PhotoSense');
  });
});

describe('LogsPanel', () => {
  it('shows the lines it is given', () => {
    render(<LogsPanel lines={['Ready.', '12:00:01 Info Scanning 12 files']} />);
    expect(screen.getByText('Ready.')).toBeInTheDocument();
    expect(screen.getByText('12:00:01 Info Scanning 12 files')).toBeInTheDocument();
  });

  it('shows no more than the newest two hundred', () => {
    render(<LogsPanel lines={Array.from({ length: 250 }, (_, n) => `line ${n}`)} />);
    expect(screen.queryByText('line 49')).not.toBeInTheDocument();
    expect(screen.getByText('line 50')).toBeInTheDocument();
    expect(screen.getByText('line 249')).toBeInTheDocument();
  });
});

describe('ProgressPanel', () => {
  const progress = (overrides: Partial<ScanProgressSnapshotDto>): ScanProgressSnapshotDto => ({
    instanceId: 'scan-7', startedUtc: '2026-10-06T12:00:00Z', primaryTotal: 0, primaryProcessed: 0, secondaryTotal: 0, secondaryProcessed: 0,
    primaryPercent: 0, secondaryPercent: 0, overallPercent: 0, ...overrides,
  });
  const bar = (container: HTMLElement) => container.querySelector<HTMLElement>('.bg-emerald-500')!;

  it('is empty before any scan', () => {
    const { container } = render(<ProgressPanel />);
    expect(screen.getByText('0.0%')).toBeInTheDocument();
    expect(bar(container)).toHaveStyle({ width: '0.0%' });
    expect(screen.queryByText(/Primary/)).not.toBeInTheDocument();
  });

  it('stays at nothing until the files have been counted', () => {
    // A scan that has not counted anything yet reports itself complete: nothing out of nothing.
    render(<ProgressPanel progress={progress({ overallPercent: 100 })} />);
    expect(screen.getByText('0.0%')).toBeInTheDocument();
    expect(screen.getByText('Primary 0/0 • Secondary 0/0')).toBeInTheDocument();
  });

  it('shows how far a counted scan has come', () => {
    const { container } = render(<ProgressPanel progress={progress({ primaryTotal: 80, primaryProcessed: 34, secondaryTotal: 20, secondaryProcessed: 0, overallPercent: 34 })} />);
    expect(screen.getByText('34.0%')).toBeInTheDocument();
    expect(bar(container)).toHaveStyle({ width: '34.0%' });
    expect(screen.getByText('Primary 34/80 • Secondary 0/20')).toBeInTheDocument();
  });

  it('counts files in the second folder alone', () => {
    render(<ProgressPanel progress={progress({ secondaryTotal: 8, secondaryProcessed: 1, overallPercent: 12.5 })} />);
    expect(screen.getByText('12.5%')).toBeInTheDocument();
  });

  it('shows a finished scan as finished even when it found nothing', () => {
    render(<ProgressPanel progress={progress({ completedUtc: '2026-10-06T12:00:05Z', overallPercent: 100 })} />);
    expect(screen.getByText('100.0%')).toBeInTheDocument();
  });
});

describe('PhotoDetails', () => {
  const cell = (label: string) => screen.getByText(label).nextElementSibling as HTMLElement;

  it('gives the name, the folder and what is not known about a bare file', () => {
    render(<PhotoDetails photo={photo()} />);
    expect(cell('Name')).toHaveTextContent('IMG_4198.JPG');
    expect(cell('Date')).toHaveTextContent('No capture date');
    expect(cell('Folder')).toHaveTextContent('C:\\photos\\2024');
    expect(cell('Taken at')).toHaveTextContent('No location in this file');
    expect(cell('File')).toHaveTextContent('4032 × 3024 · JPEG · 5.8 MB');
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
    expect(screen.queryByText('Camera')).not.toBeInTheDocument();
  });

  it('names the place, with the coordinates beside it and a link to a map', () => {
    render(<PhotoDetails photo={photo({ latitude: 35.2677, longitude: -75.5424, placeName: 'Buxton, North Carolina, US', cameraModel: 'iPhone 12 Pro Max', takenOn: '2024-04-07T17:35:59' })} />);
    expect(cell('Taken at')).toHaveTextContent('Buxton, North Carolina, US35.2677° N, 75.5424° WShow on map');
    expect(screen.getByText('35.2677° N, 75.5424° W')).toHaveClass('text-neutral-400');     // secondary to the name
    expect(screen.getByRole('link', { name: 'Show on map' })).toHaveAttribute('href', 'https://www.openstreetmap.org/?mlat=35.2677&mlon=-75.5424#map=15/35.2677/-75.5424');
    expect(cell('Camera')).toHaveTextContent('iPhone 12 Pro Max');
    expect(cell('Date')).toHaveTextContent('2024');
  });

  it('gives the coordinates alone where no place is near', () => {
    render(<PhotoDetails photo={photo({ latitude: 30, longitude: -40 })} />);
    const coordinates = screen.getByText('30.0000° N, 40.0000° W');
    expect(coordinates).not.toHaveClass('text-neutral-400');
    expect(screen.getByRole('link', { name: 'Show on map' })).toBeInTheDocument();
  });
});

describe('Differences', () => {
  it('lists what sets the copy apart', () => {
    render(<Differences original={photo()} copy={photo({ fileName: 'IMG_4198 (1).JPG', folder: 'D:\\backup' })} />);
    expect(screen.getByText('Differs from the original in')).toBeInTheDocument();
    expect(screen.getAllByRole('listitem').map(li => li.textContent)).toEqual(['Name: the original is IMG_4198.JPG', 'Folder: the original is in C:\\photos\\2024']);
  });

  it('says so when nothing on record sets it apart', () => {
    render(<Differences original={photo()} copy={photo()} />);
    expect(screen.getByText('Nothing that is recorded about it.')).toBeInTheDocument();
    expect(screen.queryByRole('list')).not.toBeInTheDocument();
  });
});

describe('GroupList', () => {
  const copies = (...kept: boolean[]) => kept.map((k, n) => member({ id: `m${n}`, kept: k }));

  it.each([
    ['duplicates', 'No duplicates to show.'],
    ['similar', 'No similar shots found.'],
  ] as const)('says so when there are no %s', (mode, message) => {
    render(<GroupList groups={[]} mode={mode} onSelect={() => undefined} />);
    expect(screen.getByText(message)).toBeInTheDocument();
    expect(screen.queryByRole('list')).not.toBeInTheDocument();
  });

  it.each([
    [copies(false), '1 duplicate · 5.8 MB'],
    [copies(false, false), '2 duplicates · 5.8 MB'],
    [copies(false, true, false), '2 duplicates · 5.8 MB'],      // one of the three is marked keep
    [copies(true), '1 copy, all marked keep'],
    [copies(true, true), '2 copies, all marked keep'],
  ])('sums up a group of duplicates', (members, summary) => {
    render(<GroupList groups={[group({ members })]} mode="duplicates" onSelect={() => undefined} />);
    expect(screen.getByRole('button')).toHaveTextContent(`IMG_4198.JPG${summary}`);
  });

  it.each([
    [copies(false), '1 similar shot'],
    [copies(false, true), '2 similar shots'],
  ])('counts the shots of a similar group', (members, summary) => {
    render(<GroupList groups={[group({ members })]} mode="similar" onSelect={() => undefined} />);
    expect(screen.getByRole('button')).toHaveTextContent(`IMG_4198.JPG${summary}`);
  });

  it('shows each group by its best copy, marks the selected one and reports a click', async () => {
    const onSelect = vi.fn();
    const groups = [group(), group({ key: 'g2', keeper: photo({ id: 'p2', fileName: 'IMG_5000.HEIC' }) })];
    const { container } = render(<GroupList groups={groups} mode="duplicates" selectedKey="g2" onSelect={onSelect} />);

    const [first, second] = screen.getAllByRole('button');
    expect(first).toHaveClass('border-transparent');
    expect(second).toHaveClass('border-emerald-400');
    expect([...container.querySelectorAll('img')].map(i => i.getAttribute('src'))).toEqual([thumbnailUrl('p1'), thumbnailUrl('p2')]);

    await userEvent.click(first);
    expect(onSelect).toHaveBeenCalledExactlyOnceWith('g1');
  });
});

describe('ConfirmDialog', () => {
  const show = (busy = false) => {
    const onConfirm = vi.fn(), onCancel = vi.fn();
    const view = render(<ConfirmDialog title="Delete all duplicates?" confirmLabel="Delete 3 files" busy={busy} onConfirm={onConfirm} onCancel={onCancel}><p>They are moved, not erased.</p></ConfirmDialog>);
    return { onConfirm, onCancel, ...view };
  };

  it('asks its question and reports the answer', async () => {
    const { onConfirm, onCancel } = show();
    const dialog = screen.getByRole('alertdialog', { name: 'Delete all duplicates?' });
    expect(within(dialog).getByText('They are moved, not erased.')).toBeInTheDocument();

    await userEvent.click(within(dialog).getByRole('button', { name: 'Delete 3 files' }));
    expect(onConfirm).toHaveBeenCalledOnce();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(onCancel).toHaveBeenCalledOnce();
  });

  it('is cancelled by Escape and by no other key', () => {
    const { onCancel, unmount } = show();
    fireEvent.keyDown(window, { key: 'Enter' });
    expect(onCancel).not.toHaveBeenCalled();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onCancel).toHaveBeenCalledOnce();

    unmount();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onCancel).toHaveBeenCalledOnce();
  });

  it('cannot be answered or dismissed while the removal is under way', () => {
    const { onCancel } = show(true);
    expect(screen.getByRole('button', { name: 'Working…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onCancel).not.toHaveBeenCalled();
  });
});

describe('toasts', () => {
  it('clear themselves after eight seconds, except errors, which wait to be dismissed', () => {
    vi.useFakeTimers();
    const { result } = renderHook(() => useToasts());

    act(() => {
      result.current.push('Scan started');
      result.current.push('Moved 3 duplicates', 'success');
      result.current.push('Cannot reach the PhotoSense server.', 'error');
    });
    expect(result.current.toasts.map(t => [t.message, t.type])).toEqual([
      ['Scan started', 'info'], ['Moved 3 duplicates', 'success'], ['Cannot reach the PhotoSense server.', 'error'],
    ]);

    act(() => { vi.advanceTimersByTime(7999); });
    expect(result.current.toasts).toHaveLength(3);
    act(() => { vi.advanceTimersByTime(1); });
    expect(result.current.toasts.map(t => t.message)).toEqual(['Cannot reach the PhotoSense server.']);

    act(() => result.current.remove(result.current.toasts[0].id));
    expect(result.current.toasts).toEqual([]);
  });

  it('are coloured by kind and dismissed by a click', async () => {
    const remove = vi.fn();
    render(<Toaster remove={remove} toasts={[
      { id: 1, message: 'plain' }, { id: 2, message: 'noted', type: 'info' }, { id: 3, message: 'done', type: 'success' }, { id: 4, message: 'failed', type: 'error' },
    ]} />);

    expect(screen.getByRole('button', { name: 'plain' })).toHaveClass('bg-neutral-800');
    expect(screen.getByRole('button', { name: 'noted' })).toHaveClass('bg-neutral-800');
    expect(screen.getByRole('button', { name: 'done' })).toHaveClass('bg-emerald-900');
    expect(screen.getByRole('button', { name: 'failed' })).toHaveClass('bg-red-900');

    await userEvent.click(screen.getByRole('button', { name: 'failed' }));
    expect(remove).toHaveBeenCalledExactlyOnceWith(4);
  });
});
