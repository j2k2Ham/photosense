import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CategoriesView } from '../../components/CategoriesView';
import type { BurstDto, CategoryName, CategoryReportDto, PhotoDto } from '../../types';
import { plan } from '../fixtures';
import { photo } from './phone/groups';

const api = vi.hoisted(() => ({ useCategories: vi.fn(), useCategoryPhotos: vi.fn(), useBursts: vi.fn(), deleteChosen: vi.fn(), undoChange: vi.fn() }));
vi.mock('../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../lib/apiClient')>()), ...api }));

const MB = 1024 ** 2;
let report: CategoryReportDto | undefined;
let lists: Record<string, PhotoDto[] | undefined>;
let bursts: BurstDto[] | undefined;

/** What the categories hold: those named with how many and what they take, nothing of the rest. */
function holding(found: Partial<Record<CategoryName, [number, number]>>, unmeasured = 0): CategoryReportDto {
  const names: CategoryName[] = ['largeVideos', 'screenshots', 'bursts', 'blurry', 'messaging', 'duplicates'];
  return { categories: names.map(category => ({ category, count: found[category]?.[0] ?? 0, bytes: found[category]?.[1] ?? 0 })), unmeasured, storageTotal: null, storageFree: null };
}

beforeEach(() => {
  for (const mock of Object.values(api)) mock.mockReset();
  report = holding({ largeVideos: [1, 300 * MB], screenshots: [3, 6 * MB], bursts: [2, 2 * MB], duplicates: [9, 900 * MB] }, 2);
  lists = {
    screenshots: [photo('IMG_1.PNG', { fileSizeBytes: 3 * MB }), photo('IMG_2.PNG', { fileSizeBytes: 2 * MB }), photo('IMG_3.PNG', { fileSizeBytes: 1 * MB })],
    largeVideos: [photo('IMG_9.MOV', { isVideo: true, fileSizeBytes: 300 * MB, width: 3840, height: 2160 }), photo('IMG_8.MOV', { isVideo: true, fileSizeBytes: 250 * MB, width: 100, height: 100 })],
  };
  bursts = undefined;
  api.useCategories.mockImplementation(() => ({ data: report }));
  api.useCategoryPhotos.mockImplementation((category: string) => ({ data: lists[category] }));
  api.useBursts.mockImplementation(() => ({ data: bursts }));
  api.deleteChosen.mockImplementation(async (ids: string[]) => ({ deleted: ids.length, bytes: ids.length * MB, skipped: 0, problems: [], batchId: 'b1' }));
  api.undoChange.mockResolvedValue({ restored: 1, skipped: 0, problems: [] });
});

const categories = () => within(screen.getByRole('navigation', { name: 'Categories' }));
const question = () => screen.getByRole('alertdialog');
const lastAction = (notify: ReturnType<typeof vi.fn>) => (notify.mock.lastCall![2] as { label: string; run(): void });

describe('Clean up\'s categories on a computer', () => {
  it('lists every category but the duplicates, largest first, and shows the largest until another is chosen', async () => {
    render(<CategoriesView notify={vi.fn()} />);
    expect(categories().getAllByRole('button').map(b => b.textContent)).toEqual(['Large videos1 item · 300.0 MB', 'Screenshots3 items · 6.0 MB',
      'Bursts2 extra shots · 2.0 MB', 'BlurryScan again to find blurry shots']);
    expect(categories().getByRole('button', { name: /^Large videos/ })).toHaveAttribute('aria-current', 'true');
    const videos = screen.getByRole('region', { name: 'Large videos' });
    expect(within(videos).getByText('300.0 MB · 4K')).toBeInTheDocument();
    expect(within(videos).getByText('250.0 MB')).toBeInTheDocument();
    expect(within(videos).getByRole('button', { name: 'Delete 0 files' })).toBeDisabled();
    await userEvent.click(categories().getByRole('button', { name: /^Screenshots/ }));
    expect(screen.getByRole('region', { name: 'Screenshots' })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Large videos' })).not.toBeInTheDocument();
  });

  it('says it is loading, and shows nothing until there is a category', () => {
    report = undefined;
    const { rerender } = render(<CategoriesView notify={vi.fn()} />);
    expect(screen.getByRole('status')).toHaveTextContent('Loading…');
    report = { ...holding({}), categories: [] };
    rerender(<CategoriesView notify={vi.fn()} />);
    expect(screen.queryByRole('region')).not.toBeInTheDocument();
  });

  it('chooses every screenshot to start with, keeps one clicked, selects all or none, and deletes after a question that names them', async () => {
    const notify = vi.fn();
    render(<CategoriesView plan={plan()} notify={notify} />);
    await userEvent.click(categories().getByRole('button', { name: /^Screenshots/ }));
    const pane = within(screen.getByRole('region', { name: 'Screenshots' }));
    expect(pane.getByText(/of 3 files · 6.0 MB/)).toBeInTheDocument();
    await userEvent.click(pane.getByRole('button', { name: 'IMG_2.PNG: to delete' }));
    expect(pane.getByRole('button', { name: 'IMG_2.PNG: kept' })).toHaveAttribute('aria-pressed', 'false');
    await userEvent.click(pane.getByRole('button', { name: 'Select all' }));
    await userEvent.click(pane.getByRole('button', { name: 'Select none' }));
    expect(pane.getByRole('button', { name: 'Delete 0 files' })).toBeDisabled();
    await userEvent.click(pane.getByRole('button', { name: 'Select all' }));
    await userEvent.click(pane.getByRole('button', { name: 'Delete 3 files' }));
    expect(question()).toHaveAccessibleName('Delete 3 files?');
    expect(within(question()).getByText('IMG_1.PNG, IMG_2.PNG and IMG_3.PNG go, 6.0 MB in all.')).toBeInTheDocument();
    expect(within(question()).getByText(/inside the scanned folder, and Undo puts them back\. .*Free plan: 38 removals left today\./)).toBeInTheDocument();
    await userEvent.click(within(question()).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    await userEvent.click(pane.getByRole('button', { name: 'Delete 3 files' }));
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 3 files' }));
    expect(api.deleteChosen).toHaveBeenCalledWith(['IMG_1.PNG', 'IMG_2.PNG', 'IMG_3.PNG']);
    await waitFor(() => expect(notify).toHaveBeenCalledWith('Moved 3 files (3.0 MB) to _PhotoSense_Removed.', 'ok', expect.objectContaining({ label: 'Undo' })));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    lastAction(notify).run();
    await waitFor(() => expect(notify).toHaveBeenLastCalledWith('Put 1 file back where it was.', 'ok'));
    expect(api.undoChange).toHaveBeenCalledWith('b1');
  });

  it('names one file alone, and says what was left alone or could not come back', async () => {
    lists.screenshots = [photo('IMG_1.PNG', { fileSizeBytes: MB })];
    api.deleteChosen.mockResolvedValueOnce({ deleted: 1, bytes: MB, skipped: 1, problems: ['IMG_1.PNG changed since the scan.'], batchId: 'b2' });
    api.undoChange.mockResolvedValueOnce({ restored: 2, skipped: 1, problems: ['A file of that name is there now.'] });
    const notify = vi.fn();
    render(<CategoriesView notify={notify} />);
    await userEvent.click(categories().getByRole('button', { name: /^Screenshots/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Delete 1 file' }));
    expect(question()).toHaveAccessibleName('Delete IMG_1.PNG?');
    expect(within(question()).getByText('IMG_1.PNG goes, 1.0 MB in all.')).toBeInTheDocument();
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 1 file' }));
    await waitFor(() => expect(notify).toHaveBeenCalledWith('1 left alone. IMG_1.PNG changed since the scan.', 'error'));
    expect(notify).toHaveBeenCalledWith('Moved IMG_1.PNG (1.0 MB) to _PhotoSense_Removed.', 'ok', expect.anything());
    notify.mock.calls.find(c => c[2])![2].run();
    await waitFor(() => expect(notify).toHaveBeenCalledWith('1 could not be put back. A file of that name is there now.', 'error'));
    expect(notify).toHaveBeenCalledWith('Put 2 files back where they were.', 'ok');
  });

  it('says why nothing moved, and nothing of Undo when there is none', async () => {
    const notify = vi.fn();
    render(<CategoriesView notify={notify} />);
    await userEvent.click(screen.getByRole('button', { name: 'IMG_9.MOV: kept' }));
    api.deleteChosen.mockRejectedValueOnce(new Error('The disk is full.'));
    await userEvent.click(screen.getByRole('button', { name: 'Delete 1 file' }));
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 1 file' }));
    await waitFor(() => expect(notify).toHaveBeenCalledWith('The disk is full.', 'error'));
    api.deleteChosen.mockRejectedValueOnce('gone');
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 1 file' }));
    await waitFor(() => expect(notify).toHaveBeenCalledWith('gone', 'error'));
    api.deleteChosen.mockResolvedValueOnce({ deleted: 0, bytes: 0, skipped: 0, problems: [] });
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 1 file' }));
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    expect(notify).toHaveBeenCalledTimes(2);
  });

  it('says why an undo failed', async () => {
    api.undoChange.mockRejectedValueOnce(new Error('No such change.')).mockRejectedValueOnce('lost');
    const notify = vi.fn();
    api.deleteChosen.mockResolvedValue({ deleted: 1, bytes: MB, skipped: 0, problems: [], batchId: 'b3' });
    render(<CategoriesView notify={notify} />);
    await userEvent.click(screen.getByRole('button', { name: 'IMG_9.MOV: kept' }));
    await userEvent.click(screen.getByRole('button', { name: 'Delete 1 file' }));
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 1 file' }));
    await waitFor(() => expect(notify).toHaveBeenCalledTimes(1));
    const undo = lastAction(notify);
    undo.run();
    await waitFor(() => expect(notify).toHaveBeenLastCalledWith('No such change.', 'error'));
    undo.run();
    await waitFor(() => expect(notify).toHaveBeenLastCalledWith('lost', 'error'));
  });

  it('says what was left alone or not put back without a reason when the service gives none, and offers no Undo without a change to undo', async () => {
    api.deleteChosen.mockResolvedValueOnce({ deleted: 1, bytes: MB, skipped: 1, problems: [] }).mockResolvedValueOnce({ deleted: 1, bytes: MB, skipped: 0, problems: [], batchId: 'b4' });
    api.undoChange.mockResolvedValueOnce({ restored: 0, skipped: 1, problems: [] });
    const notify = vi.fn();
    render(<CategoriesView notify={notify} />);
    const video = () => screen.getByRole('button', { name: /^IMG_9\.MOV/ });
    await userEvent.click(video());
    await userEvent.click(video());
    expect(video()).toHaveAttribute('aria-pressed', 'false');
    await userEvent.click(video());
    await userEvent.click(screen.getByRole('button', { name: 'Delete 1 file' }));
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 1 file' }));
    await waitFor(() => expect(notify).toHaveBeenCalledWith('1 left alone.', 'error'));
    expect(notify).toHaveBeenCalledWith('Moved IMG_9.MOV (1.0 MB) to _PhotoSense_Removed.', 'ok', undefined);
    await userEvent.click(video());
    await userEvent.click(screen.getByRole('button', { name: 'Delete 1 file' }));
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 1 file' }));
    await waitFor(() => expect(notify).toHaveBeenCalledTimes(3));
    lastAction(notify).run();
    await waitFor(() => expect(notify).toHaveBeenLastCalledWith('1 could not be put back.', 'error'));
    expect(notify).toHaveBeenCalledTimes(4);
  });

  it('over the free plan\'s limit, asks with the limit\'s own question, which deletes the largest that fit', async () => {
    const notify = vi.fn();
    render(<CategoriesView plan={plan({ leftToday: 2 })} notify={notify} />);
    await userEvent.click(categories().getByRole('button', { name: /^Screenshots/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Delete 3 files' }));
    expect(question()).toHaveAccessibleName('2 free removals left today');
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 2 now' }));
    expect(api.deleteChosen).toHaveBeenCalledWith(['IMG_1.PNG', 'IMG_2.PNG']);
    await userEvent.click(screen.getByRole('button', { name: 'Delete 3 files' }));
    await userEvent.click(within(question()).getByRole('button', { name: 'Not now' }));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('says when a category is loading or has nothing left', async () => {
    lists.largeVideos = undefined;
    const { rerender } = render(<CategoriesView notify={vi.fn()} />);
    expect(within(screen.getByRole('region', { name: 'Large videos' })).getByRole('status')).toHaveTextContent('Loading…');
    lists.largeVideos = [];
    rerender(<CategoriesView notify={vi.fn()} />);
    expect(screen.getByText('Nothing left to review.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Select all' })).toBeDisabled();
  });
});

describe('bursts on a computer', () => {
  const shot = (name: string, more: Partial<PhotoDto> = {}) => photo(name, { fileSizeBytes: MB, ...more });
  const open = () => userEvent.click(categories().getByRole('button', { name: /^Bursts/ }));

  it('frames the shot kept in each burst, keeps another clicked instead, and deletes the rest, saying which stay', async () => {
    bursts = [
      { keeper: 'B2', shots: [shot('B1'), shot('B2'), shot('B3')] },
      { keeper: 'C1', shots: [shot('C1', { takenOn: undefined, placeName: undefined }), shot('C2', { takenOn: undefined, placeName: undefined })] },
    ];
    const notify = vi.fn();
    render(<CategoriesView notify={notify} />);
    await open();
    const first = within(screen.getByRole('region', { name: 'Dec 27, 2023' }));
    expect(first.getByRole('heading', { level: 3 })).toHaveTextContent('Dec 27, 2023 · Anaconda, Montana, US · 3 shots');
    expect(within(screen.getByRole('region', { name: 'C1' })).getByRole('heading', { level: 3 })).toHaveTextContent('C1 · 2 shots');
    expect(first.getByRole('button', { name: 'B2: kept' })).toHaveAttribute('aria-pressed', 'false');
    expect(first.getAllByText('Keep')).toHaveLength(1);
    await userEvent.click(first.getByRole('button', { name: 'B3: to delete' }));
    expect(first.getByRole('button', { name: 'B3: kept' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Delete 3 extra shots' }));
    expect(within(question()).getByText('B1, B2 and C2 go, 3.0 MB in all.')).toBeInTheDocument();
    expect(within(question()).getByText('The shot kept of each of the 2 bursts stays.')).toBeInTheDocument();
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 3 files' }));
    expect(api.deleteChosen).toHaveBeenCalledWith(['B1', 'B2', 'C2']);
    await waitFor(() => expect(notify).toHaveBeenCalledWith('Moved 3 files (3.0 MB) to _PhotoSense_Removed. The shot kept of each burst stays where it is.', 'ok', expect.anything()));
    expect(first.getByRole('button', { name: 'B2: kept' })).toBeInTheDocument();
  });

  it('names the one shot that stays of a single burst', async () => {
    bursts = [{ keeper: 'B1', shots: [shot('B1'), shot('B2')] }];
    const notify = vi.fn();
    render(<CategoriesView notify={notify} />);
    await open();
    await userEvent.click(screen.getByRole('button', { name: 'Delete 1 extra shot' }));
    expect(within(question()).getByText('B1 stays.')).toBeInTheDocument();
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 1 file' }));
    await waitFor(() => expect(notify).toHaveBeenCalledWith('Moved B2 (1.0 MB) to _PhotoSense_Removed. B1 stays where it is.', 'ok', expect.anything()));
  });

  it('says it is loading, or that none is left', async () => {
    const { rerender } = render(<CategoriesView notify={vi.fn()} />);
    await open();
    expect(within(screen.getByRole('region', { name: 'Bursts' })).getByRole('status')).toHaveTextContent('Loading…');
    bursts = [];
    rerender(<CategoriesView notify={vi.fn()} />);
    expect(screen.getByText('Nothing left to review.')).toBeInTheDocument();
    expect(screen.getByText(/of 0 bursts/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete 0 extra shots' })).toBeDisabled();
  });
});
