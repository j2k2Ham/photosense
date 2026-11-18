import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BurstsScreen, CategoryGrid, FreeUpSpace, LargeVideosScreen } from '../../../components/phone/Categories';
import type { Platform } from '../../../lib/phone';
import type { BurstDto, CategoryReportDto, CompressibleVideoDto, CompressionStateDto, PhotoDto } from '../../../types';
import { plan } from '../../fixtures';
import { frame, gate, inFrame } from './frame';
import { photo } from './groups';

const api = vi.hoisted(() => ({ useCategories: vi.fn(), useCategoryPhotos: vi.fn(), useBursts: vi.fn(), deleteChosen: vi.fn(), useCompressVideos: vi.fn(), useCompression: vi.fn(),
  startCompression: vi.fn(), cancelCompression: vi.fn() }));
vi.mock('../../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/apiClient')>()), ...api }));

// What the service holds of each category; a category missing is still being read.
let lists: Record<string, PhotoDto[] | undefined>;
let bursts: BurstDto[] | undefined;
let report: CategoryReportDto | undefined;
let videos: CompressibleVideoDto[] | undefined;
let compression: CompressionStateDto | undefined;
const MB = 1024 ** 2;

beforeEach(() => {
  for (const mock of Object.values(api)) mock.mockReset();
  lists = {};
  bursts = undefined;
  report = undefined;
  videos = undefined;
  compression = undefined;
  api.useCompressVideos.mockImplementation(() => ({ data: videos }));
  api.useCompression.mockImplementation(() => ({ data: compression }));
  api.startCompression.mockResolvedValue(undefined);
  api.cancelCompression.mockResolvedValue(undefined);
  api.useCategoryPhotos.mockImplementation((category: string) => ({ data: lists[category] }));
  api.useBursts.mockImplementation(() => ({ data: bursts }));
  api.useCategories.mockImplementation(() => ({ data: report }));
  api.deleteChosen.mockImplementation(async (ids: string[]) => ({ deleted: ids.length, bytes: 0, skipped: 0, problems: [] }));
});

/** A category's screen in a phone's frame, with the plan's gate given. */
function show(ui: React.ReactElement, { platform = 'ios', tablet = false, planGate = gate() }: { platform?: Platform; tablet?: boolean; planGate?: ReturnType<typeof gate> } = {}) {
  return { ...inFrame(ui, frame(platform, { tablet, form: tablet ? 'tablet' : 'phone' }), planGate), planGate };
}

const foot = (name: string | RegExp) => screen.getByRole('button', { name });
const tiles = (list: string) => within(screen.getByRole('list', { name: list })).getAllByRole('button');

describe('a category\'s grid (19)', () => {
  const shots = () => [photo('IMG_1.PNG', { fileSizeBytes: 3 * MB }), photo('IMG_2.PNG', { fileSizeBytes: 1 * MB }), photo('IMG_3.PNG', { fileSizeBytes: 2 * MB })];

  it('says it is looking until the service has said, then shows every picture chosen, what they take, and where they were found', async () => {
    const notify = vi.fn(), onBack = vi.fn();
    const { again } = show(<CategoryGrid category="screenshots" title="Screenshots" notify={notify} onBack={onBack} />);
    expect(screen.getByRole('status')).toHaveTextContent('Looking for them…');
    expect(foot('Nothing selected')).toBeDisabled();
    expect(screen.queryByRole('button', { name: /select all/i })).not.toBeInTheDocument();
    lists.screenshots = shots();
    again(<CategoryGrid category="screenshots" title="Screenshots" notify={notify} onBack={onBack} />);
    expect(screen.getByRole('heading', { level: 1, name: 'Screenshots' })).toBeInTheDocument();
    expect(screen.getByText('6.0 MB')).toBeInTheDocument();
    expect(screen.getByText('selected from 3 screenshots')).toBeInTheDocument();
    expect(screen.getByText('Found on this iPhone. Nothing was uploaded. Tap a photo to keep it.')).toBeInTheDocument();
    expect(tiles('Screenshots').map(t => t.getAttribute('aria-label'))).toEqual(['IMG_1.PNG: to delete', 'IMG_2.PNG: to delete', 'IMG_3.PNG: to delete']);
    await userEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(onBack).toHaveBeenCalledOnce();
  });

  it('keeps a picture tapped, selects or deselects them all, and deletes those chosen through the plan\'s gate, saying where they wait', async () => {
    lists.screenshots = shots();
    const notify = vi.fn();
    const { planGate } = show(<CategoryGrid category="screenshots" title="Screenshots" notify={notify} onBack={vi.fn()} />);
    await userEvent.click(screen.getByRole('button', { name: 'IMG_2.PNG: to delete' }));
    expect(screen.getByRole('button', { name: 'IMG_2.PNG: kept' })).toHaveAttribute('aria-pressed', 'false');
    expect(screen.getByText('5.0 MB')).toBeInTheDocument();
    expect(foot('Delete 2 items')).toBeEnabled();
    await userEvent.click(screen.getByRole('button', { name: 'Select all' }));
    expect(foot('Delete 3 items')).toBeEnabled();
    await userEvent.click(screen.getByRole('button', { name: 'Deselect all' }));
    expect(foot('Nothing selected')).toBeDisabled();
    expect(screen.getByText('0 KB')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'IMG_1.PNG: kept' }));
    expect(foot('Delete 1 item')).toBeEnabled();
    await userEvent.click(screen.getByRole('button', { name: 'Select all' }));
    await userEvent.click(foot('Delete 3 items'));
    expect(planGate.remove).toHaveBeenCalledWith(3, 'items', expect.any(Function), true);
    expect(api.deleteChosen).toHaveBeenCalledWith(['IMG_1.PNG', 'IMG_3.PNG', 'IMG_2.PNG']);
    await waitFor(() => expect(notify).toHaveBeenCalledWith('3 items deleted. They wait in Recently Deleted for 30 days.', 'ok'));
  });

  it('deletes only the largest the free plan has left today, and says the plan\'s line under the button', async () => {
    lists.blurry = shots();
    const planGate = gate({ plan: plan(), remove: vi.fn((_n: number, _l: string, run: (allowed: number) => void) => run(1)) });
    show(<CategoryGrid category="blurry" title="Blurry" notify={vi.fn()} onBack={vi.fn()} />, { planGate });
    expect(screen.getByText('selected from 3 blurry shots')).toBeInTheDocument();
    expect(screen.getByText(/^Free plan: 38 removals left today\.$/)).toBeInTheDocument();
    await userEvent.click(foot('Delete 3 items'));
    expect(api.deleteChosen).toHaveBeenCalledWith(['IMG_1.PNG']);
  });

  it('says why nothing was deleted, and on Android where the tick is, in its own words', async () => {
    lists.messaging = [photo('VID-1.MP4', { isVideo: true, durationSeconds: 47 }), photo('VID-2.MP4', { isVideo: true })];
    const notify = vi.fn();
    show(<CategoryGrid category="messaging" title="Messaging apps" notify={notify} onBack={vi.fn()} />, { platform: 'android' });
    expect(screen.getByText('selected from 2 photos and videos')).toBeInTheDocument();
    expect(screen.getByText('Found on this phone. Nothing was uploaded. Tap a photo to keep it.')).toBeInTheDocument();
    expect(screen.getByText('0:47')).toBeInTheDocument();
    api.deleteChosen.mockRejectedValueOnce(new Error('You chose not to delete them.'));
    await userEvent.click(foot('Delete 2 items'));
    await waitFor(() => expect(notify).toHaveBeenCalledWith('You chose not to delete them.', 'error'));
    api.deleteChosen.mockRejectedValueOnce('gone');
    await userEvent.click(foot('Delete 2 items'));
    await waitFor(() => expect(notify).toHaveBeenCalledWith('gone', 'error'));
  });

  it('says when there is nothing left', () => {
    lists.screenshots = [];
    show(<CategoryGrid category="screenshots" title="Screenshots" notify={vi.fn()} onBack={vi.fn()} />);
    expect(screen.getByText('Nothing left to review.')).toBeInTheDocument();
    expect(screen.getByText('selected from 0 screenshots')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /select all/i })).not.toBeInTheDocument();
  });
});

describe('Bursts (20)', () => {
  const shot = (name: string, more: Partial<PhotoDto> = {}) => photo(name, { fileSizeBytes: MB, ...more });

  it('shows each burst with the shot kept, keeps another tapped instead, and deletes the extra shots', async () => {
    bursts = [
      { keeper: 'B2', shots: [shot('B1'), shot('B2'), shot('B3')] },
      { keeper: 'C1', shots: [shot('C1', { takenOn: undefined, placeName: undefined }), shot('C2', { takenOn: undefined, placeName: undefined })] },
    ];
    const notify = vi.fn(), onBack = vi.fn();
    show(<BurstsScreen notify={notify} onBack={onBack} />);
    expect(screen.getByText('from 3 extra shots in 2 bursts')).toBeInTheDocument();
    expect(screen.getByText('3.0 MB')).toBeInTheDocument();
    expect(screen.getByText('PhotoSense keeps the sharpest shot in each burst. Tap another shot to keep it instead.')).toBeInTheDocument();
    const first = screen.getByRole('region', { name: 'Dec 27, 2023' });
    expect(within(first).getByText('Anaconda, Montana, US · 3 shots')).toBeInTheDocument();
    expect(within(first).getByRole('button', { name: 'B2: kept' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(first).getByText('Keep')).toBeInTheDocument();
    const second = screen.getByRole('region', { name: 'C1' });
    expect(within(second).getByText('2 shots')).toBeInTheDocument();
    await userEvent.click(within(first).getByRole('button', { name: 'B3: to delete' }));
    expect(within(first).getByRole('button', { name: 'B3: kept' })).toBeInTheDocument();
    expect(within(first).getByRole('button', { name: 'B2: to delete' })).toBeInTheDocument();
    await userEvent.click(foot('Delete 3 extra shots'));
    expect(api.deleteChosen).toHaveBeenCalledWith(['B1', 'B2', 'C2']);
    await waitFor(() => expect(notify).toHaveBeenCalledWith('3 items deleted. They wait in Recently Deleted for 30 days.', 'ok'));
    expect(within(first).getByRole('button', { name: 'B2: kept' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(onBack).toHaveBeenCalledOnce();
  });

  it('says it is looking, then one extra shot, or nothing left', () => {
    const { again } = show(<BurstsScreen notify={vi.fn()} onBack={vi.fn()} />);
    expect(screen.getByRole('status')).toHaveTextContent('Looking for them…');
    bursts = [{ keeper: 'B1', shots: [shot('B1'), shot('B2')] }];
    again(<BurstsScreen notify={vi.fn()} onBack={vi.fn()} />);
    expect(screen.getByText('from 1 extra shot in 1 burst')).toBeInTheDocument();
    expect(foot('Delete 1 extra shot')).toBeEnabled();
    bursts = [];
    again(<BurstsScreen notify={vi.fn()} onBack={vi.fn()} />);
    expect(screen.getByText('Nothing left to review.')).toBeInTheDocument();
    expect(foot('Nothing to delete')).toBeDisabled();
  });
});

describe('Large videos (21) and Compress (22)', () => {
  const big = () => photo('IMG_1.MOV', { isVideo: true, fileSizeBytes: 900 * MB, width: 3840, height: 2160, durationSeconds: 300 });
  const small = () => photo('IMG_2.MOV', { isVideo: true, fileSizeBytes: 300 * MB, width: 640, height: 480, takenOn: undefined, placeName: undefined });
  const two = (): CompressibleVideoDto[] => [
    { video: big(), estimates: { high: 400 * MB, balanced: 120 * MB, small: 50 * MB } },
    { video: small(), estimates: { high: null, balanced: null, small: null } },
  ];
  const running = (more: Partial<CompressionStateDto> = {}): CompressionStateDto => ({
    stage: 'running', quality: 'balanced', total: 2, done: 0, current: 'IMG_1.MOV', progress: 0.25, results: [], saved: 0, originalsDeleted: null, problem: null, ...more,
  });
  const pro = () => gate({ plan: plan({ tier: 'pro', leftToday: null }) });
  const row = (name: RegExp) => within(screen.getByRole('list', { name: 'Large videos' })).getByRole('button', { name });

  it('lists each video with where and when, its frame, its size and about what its copy takes, and compresses them all at Balanced on Pro', async () => {
    videos = two();
    const notify = vi.fn(), onBack = vi.fn();
    show(<LargeVideosScreen notify={notify} onBack={onBack} />, { planGate: pro() });
    expect(screen.getByText('1.17 GB')).toBeInTheDocument();
    expect(screen.getByText('in 2 videos over 200 MB')).toBeInTheDocument();
    const card = within(screen.getByRole('region', { name: 'Compress all' }));
    expect(card.getByText('Compress all 1')).toBeInTheDocument();
    expect(card.getByText('Saves about 780.0 MB at Balanced quality')).toBeInTheDocument();
    expect(card.queryByText('PRO')).not.toBeInTheDocument();
    expect(row(/^Anaconda/)).toHaveTextContent('Anaconda, Montana, USDec 27, 2023 · 4K900.0 MB→ 120.0 MB');
    expect(row(/^IMG_2/)).toHaveTextContent('IMG_2.MOV300.0 MB');
    await userEvent.click(card.getByRole('button', { name: 'Compress' }));
    expect(api.startCompression).toHaveBeenCalledWith(['IMG_1.MOV'], 'balanced');
    await userEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(onBack).toHaveBeenCalledOnce();
  });

  it('on the free plan marks Compress all as Pro and opens the Pro sheet, which starts it once bought, and says why it could not start', async () => {
    videos = two();
    const notify = vi.fn();
    const { planGate } = show(<LargeVideosScreen notify={notify} onBack={vi.fn()} />, { planGate: gate({ plan: plan() }) });
    const card = within(screen.getByRole('region', { name: 'Compress all' }));
    expect(card.getByText('PRO')).toBeInTheDocument();
    await userEvent.click(card.getByRole('button', { name: 'Compress' }));
    expect(planGate.offer).toHaveBeenCalledWith('compress', expect.any(Function));
    expect(api.startCompression).not.toHaveBeenCalled();
    api.startCompression.mockRejectedValueOnce(new Error('Video compression is part of Pro.'));
    (planGate.offer as ReturnType<typeof vi.fn>).mock.lastCall![1]();
    await waitFor(() => expect(notify).toHaveBeenCalledWith('Video compression is part of Pro.', 'error'));
  });

  it('shows how far compressing has got with Cancel while it runs, and marks the videos compressed once it is done', async () => {
    videos = two();
    compression = running();
    const { again } = show(<LargeVideosScreen notify={vi.fn()} onBack={vi.fn()} />, { planGate: pro() });
    const card = within(screen.getByRole('region', { name: 'Compress all' }));
    expect(card.getByText('Compressing 1 of 2')).toBeInTheDocument();
    expect(card.getByText('IMG_1.MOV')).toBeInTheDocument();
    expect(card.getByRole('progressbar', { name: 'Compressing' })).toHaveAttribute('aria-valuenow', '25');
    await userEvent.click(card.getByRole('button', { name: 'Cancel' }));
    expect(api.cancelCompression).toHaveBeenCalledOnce();
    compression = running({ done: 2, total: 2 });
    again(<LargeVideosScreen notify={vi.fn()} onBack={vi.fn()} />);
    expect(screen.getByText('Compressing 2 of 2')).toBeInTheDocument();

    compression = { ...running(), stage: 'done', done: 1, total: 1, results: [{ id: 'IMG_1.MOV', fileName: 'IMG_1.MOV', before: 900 * MB, after: 120 * MB }] };
    again(<LargeVideosScreen notify={vi.fn()} onBack={vi.fn()} />);
    expect(row(/^Anaconda/)).toHaveTextContent('Compressed');
  });

  it('says it is looking, then of one video, or that none is left, with nothing to compress', () => {
    const { again } = show(<LargeVideosScreen notify={vi.fn()} onBack={vi.fn()} />);
    expect(screen.getByRole('status')).toHaveTextContent('Looking for them…');
    videos = [{ video: big(), estimates: { high: null, balanced: null, small: null } }];
    again(<LargeVideosScreen notify={vi.fn()} onBack={vi.fn()} />);
    expect(screen.getByText('in 1 video over 200 MB')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Compress all' })).not.toBeInTheDocument();
    videos = [];
    again(<LargeVideosScreen notify={vi.fn()} onBack={vi.fn()} />);
    expect(screen.getByText('in 0 videos over 200 MB')).toBeInTheDocument();
    expect(screen.getByText('Nothing left to review.')).toBeInTheDocument();
  });

  it('opens a video before and after, at the quality chosen, and compresses it, going back once it has ended', async () => {
    videos = two();
    const { again } = show(<LargeVideosScreen notify={vi.fn()} onBack={vi.fn()} />, { planGate: pro() });
    await userEvent.click(row(/^Anaconda/));
    expect(screen.getByRole('heading', { level: 1, name: 'Compress video' })).toBeInTheDocument();
    expect(screen.getByText('Saves about 780.0 MB')).toBeInTheDocument();
    expect(screen.getByText('900.0 MB → about 120.0 MB')).toBeInTheDocument();
    expect(screen.getAllByRole('figure').map(f => [f.getAttribute('aria-label'), f.querySelector('figcaption')!.textContent])).toEqual([['Original', 'Original · 900.0 MB'], ['Compressed', 'About 120.0 MB']]);
    expect(screen.getByText('Anaconda, Montana, US')).toBeInTheDocument();
    expect(screen.getByText('Dec 27, 2023 · 5:00')).toBeInTheDocument();
    expect(screen.getByText('1080p, HEVC')).toBeInTheDocument();
    expect(screen.getByText('Moves to Recently Deleted')).toBeInTheDocument();
    expect(screen.getByText('Kept')).toBeInTheDocument();
    expect(screen.queryByText('Video compression is part of Pro.')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('radio', { name: 'Small' }));
    expect(screen.getByText('Saves about 850.0 MB')).toBeInTheDocument();
    expect(screen.getByText('720p, HEVC')).toBeInTheDocument();
    await userEvent.click(foot('Compress'));
    expect(api.startCompression).toHaveBeenCalledWith(['IMG_1.MOV'], 'small');

    // While it runs: how far, and Cancel.
    compression = running({ total: 1, progress: 0.4 });
    again(<LargeVideosScreen notify={vi.fn()} onBack={vi.fn()} />);
    expect(screen.getByText('Compressing IMG_1.MOV · 40%')).toBeInTheDocument();
    await userEvent.click(foot('Cancel'));
    expect(api.cancelCompression).toHaveBeenCalledOnce();
    compression = { ...running(), stage: 'done' };
    again(<LargeVideosScreen notify={vi.fn()} onBack={vi.fn()} />);
    expect(screen.getByRole('list', { name: 'Large videos' })).toBeInTheDocument();
  });

  it('says when a copy would not be worth making, deletes the video instead, and goes back with Back', async () => {
    videos = two();
    const notify = vi.fn();
    show(<LargeVideosScreen notify={notify} onBack={vi.fn()} />, { platform: 'android', planGate: pro() });
    await userEvent.click(row(/^IMG_2/));
    expect(screen.getByText('This video is about as small as it gets at Balanced quality.')).toBeInTheDocument();
    expect(foot('Compress')).toBeDisabled();
    expect(screen.getByRole('figure', { name: 'Compressed' })).toHaveTextContent('No smaller copy');
    // Neither its place nor its date known: its name, and nothing beside it.
    expect(screen.getByText('IMG_2.MOV')).toBeInTheDocument();
    expect(screen.getByText('Moves to the bin')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('radio', { name: 'High' }));
    expect(screen.getByText('This video is about as small as it gets at High quality.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('radio', { name: 'Small' }));
    expect(screen.getByText('This video is about as small as it gets at Small quality.')).toBeInTheDocument();
    await userEvent.click(foot('Delete this video instead'));
    expect(api.deleteChosen).toHaveBeenCalledWith(['IMG_2.MOV']);
    await waitFor(() => expect(notify).toHaveBeenCalledWith('1 item deleted. It waits in the bin for 30 days.', 'ok'));
    expect(screen.getByRole('list', { name: 'Large videos' })).toBeInTheDocument();

    await userEvent.click(row(/^Anaconda/));
    await userEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(screen.getByRole('list', { name: 'Large videos' })).toBeInTheDocument();
    // The phone's own Back, or a swipe from the edge, closes it too.
    await userEvent.click(row(/^Anaconda/));
    await act(async () => { history.back(); await new Promise(r => setTimeout(r, 20)); });
    expect(screen.getByRole('list', { name: 'Large videos' })).toBeInTheDocument();
  });

  it('on the free plan says compressing is part of Pro and opens the Pro sheet, and says why it could not start', async () => {
    videos = two();
    const notify = vi.fn();
    const { planGate } = show(<LargeVideosScreen notify={notify} onBack={vi.fn()} />, { planGate: gate({ plan: plan() }) });
    await userEvent.click(row(/^Anaconda/));
    expect(screen.getByText('Video compression is part of Pro.')).toBeInTheDocument();
    await userEvent.click(foot('Compress'));
    expect(planGate.offer).toHaveBeenCalledWith('compress', expect.any(Function));
    api.startCompression.mockRejectedValueOnce('The phone could not compress the video.');
    (planGate.offer as ReturnType<typeof vi.fn>).mock.lastCall![1]();
    await waitFor(() => expect(notify).toHaveBeenCalledWith('The phone could not compress the video.', 'error'));
  });
});

describe('Free up space (24)', () => {
  const boxes = () => within(screen.getByRole('list', { name: 'What to delete' })).getAllByRole('checkbox');

  it('says how full the phone is and what the three quickest kinds would free, each photo once, and deletes those ticked', async () => {
    report = { categories: [], unmeasured: 0, storageTotal: 1000 * MB, storageFree: 130 * MB };
    const notify = vi.fn(), onBack = vi.fn();
    const { again } = show(<FreeUpSpace notify={notify} onBack={onBack} />);
    expect(screen.getByText('iPhone storage is 87% full')).toBeInTheDocument();
    expect(screen.getByText('can be freed from 0 items')).toBeInTheDocument();
    expect(boxes().map(b => b.textContent)).toEqual(['Duplicates…', 'Screenshots…', 'Blurry…']);
    lists = { duplicates: [photo('a', { fileSizeBytes: 10 * MB }), photo('b', { fileSizeBytes: 20 * MB })], screenshots: [photo('b', { fileSizeBytes: 20 * MB }), photo('c', { fileSizeBytes: 5 * MB })], blurry: [photo('d', { fileSizeBytes: 1 * MB })] };
    again(<FreeUpSpace notify={notify} onBack={onBack} />);
    expect(boxes().map(b => b.textContent)).toEqual(['Duplicates2 items · 30.0 MB', 'Screenshots2 items · 25.0 MB', 'Blurry1 item · 1.0 MB']);
    expect(screen.getByText('can be freed from 4 items')).toBeInTheDocument();
    expect(screen.getByText('36.0 MB selected')).toBeInTheDocument();
    expect(screen.getByText('0.9 of 1 GB used')).toBeInTheDocument();
    expect(screen.getByText('These three are the quickest to check. Large videos and similar shots are in Clean up. Low-storage alerts come at most once a week.')).toBeInTheDocument();
    await userEvent.click(boxes()[0]);
    expect(boxes()[0]).toHaveAttribute('aria-checked', 'false');
    expect(screen.getByText('can be freed from 3 items')).toBeInTheDocument();
    await userEvent.click(boxes()[1]);
    await userEvent.click(boxes()[2]);
    expect(foot('Nothing selected')).toBeDisabled();
    await userEvent.click(boxes()[2]);
    expect(screen.getByText('can be freed from 1 item')).toBeInTheDocument();
    await userEvent.click(boxes()[0]);
    await userEvent.click(foot('Delete 3 items'));
    expect(api.deleteChosen).toHaveBeenCalledWith(['b', 'a', 'd']);
    await waitFor(() => expect(notify).toHaveBeenCalledWith('3 items deleted. They wait in Recently Deleted for 30 days.', 'ok'));
    await userEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(onBack).toHaveBeenCalledOnce();
  });

  it('leaves out how full the storage is when the phone has not said, and names an Android tablet as one', () => {
    lists = { duplicates: [], screenshots: [], blurry: [] };
    const { again } = show(<FreeUpSpace notify={vi.fn()} onBack={vi.fn()} />, { platform: 'android' });
    expect(screen.queryByText(/storage is/)).not.toBeInTheDocument();
    expect(boxes().map(b => b.textContent)).toEqual(['DuplicatesNothing found', 'ScreenshotsNothing found', 'BlurryNothing found']);
    expect(screen.getByText('0 KB')).toBeInTheDocument();
    expect(screen.queryByText(/used$/)).not.toBeInTheDocument();
    report = { categories: [], unmeasured: 0, storageTotal: 1000 * MB, storageFree: 500 * MB };
    again(<FreeUpSpace notify={vi.fn()} onBack={vi.fn()} />, frame('android', { tablet: true, form: 'tablet' }));
    expect(screen.getByText('Tablet storage is 50% full')).toBeInTheDocument();
  });
});
