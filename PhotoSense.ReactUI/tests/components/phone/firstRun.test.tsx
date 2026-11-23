import { act, fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PhoneApp } from '../../../components/phone/PhoneApp';
import { loadLastScan } from '../../../lib/lastScan';
import type { Platform, TabId } from '../../../lib/phone';
import type { FindingsDto, PairingStateDto, PlanDto, ScanProgressSnapshotDto } from '../../../types';
import { deferred } from '../../deferred';
import { plan } from '../../fixtures';
import { width } from './frame';

const api = vi.hoisted(() => ({
  openedNotice: vi.fn(async (): Promise<{ opens: string | null }> => ({ opens: null })), useNotices: vi.fn((): { data?: unknown } => ({})), changeNotices: vi.fn(), useCompression: vi.fn(() => ({})),
  usePhotoAccess: vi.fn(), askForPhotos: vi.fn(), openPhotoSettings: vi.fn(), libraryRoot: vi.fn(), startScan: vi.fn(), useGroupsThrough: vi.fn(),
  usePairing: vi.fn(), scanPairing: vi.fn(), enterPairingCode: vi.fn(), confirmPairing: vi.fn(), stopPairing: vi.fn(), useFound: vi.fn(), usePlan: vi.fn(), buyPlan: vi.fn(),
}));
vi.mock('../../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/apiClient')>()), ...api }));

type Access = 'everything' | 'selected' | 'nothing';
/** What the phone lets PhotoSense see now; undefined until it has said. */
let access: Access | undefined;
/** What the person answers to the phone's question. */
let answer: Access;
/** Where pairing has got to, which the stand-ins change as the phone would. */
let pairing: PairingStateDto;
let found: FindingsDto | undefined;
let currentPlan: PlanDto | undefined;
const MB = 1024 ** 2;
const stage = () => localStorage.getItem('photosense-first-run');
const pairingAt = (more: Partial<PairingStateDto>): PairingStateDto => ({ stage: 'idle', link: null, code: null, expires: null, check: null, other: null, folder: null, problem: null, ...more });

beforeEach(() => {
  for (const mock of Object.values(api)) mock.mockReset();
  access = undefined;
  answer = 'everything';
  pairing = pairingAt({});
  found = { items: [{ kind: 'duplicates', count: 264, bytes: 1900 * MB }, { kind: 'similar', count: 512, bytes: 3400 * MB }, { kind: 'largeVideos', count: 0, bytes: 0 }] };
  currentPlan = plan();
  api.usePhotoAccess.mockImplementation(() => ({ data: access && { access } }));
  api.askForPhotos.mockImplementation(async () => ({ access: (access = answer) }));
  api.openPhotoSettings.mockResolvedValue(undefined);
  api.libraryRoot.mockResolvedValue('/Photo Library');
  api.startScan.mockResolvedValue({ instanceId: 'scan-1' });
  api.useGroupsThrough.mockReturnValue({});
  api.usePairing.mockImplementation(() => ({ data: pairing }));
  api.scanPairing.mockImplementation(async () => (pairing = pairingAt({ stage: 'paired', other: 'Studio iMac', folder: 'Pictures/PhotoSense' })));
  api.enterPairingCode.mockImplementation(async () => (pairing = pairingAt({ stage: 'finding' })));
  api.confirmPairing.mockImplementation(async (same: boolean) => (pairing = same ? pairingAt({ stage: 'paired', other: 'Studio iMac', folder: 'Pictures/PhotoSense' }) : pairingAt({ stage: 'failed', problem: 'The numbers did not match.' })));
  api.stopPairing.mockResolvedValue(undefined);
  api.useFound.mockImplementation(() => ({ data: found }));
  api.usePlan.mockImplementation(() => ({ data: currentPlan }));
  width(false);
});

interface AppProps {
  platform?: Platform;
  scanning?: boolean;
  progress?: ScanProgressSnapshotDto;
  onStarted?(id: string): void;
  onStop?(): void;
  notify?(message: string, kind: string): void;
  onTab?(tab: TabId): void;
  onAbout?(topic?: string): void;
  /** About is open over the layout. */
  reading?: boolean;
}

/** The phone layout, with what the page would give it. */
function App({ platform = 'ios', scanning = false, progress, onStarted = vi.fn(), onStop = vi.fn(), notify = vi.fn(), onTab = vi.fn(), onAbout = vi.fn(), reading }: AppProps) {
  const [tab, setTab] = React.useState<TabId>('library');
  // A scan started runs, as the page has it, until the test says otherwise.
  const [started, setStarted] = React.useState(false);
  return (
    <PhoneApp platform={platform} tab={tab}
      toasts={[]} theme="dark" errorCount={0} settings={{ eraseAskTwice: true, distanceUnit: 'miles', phonesSeen: false, removeLocationWhenSharing: true }} onSettings={vi.fn()} scanning={scanning || started} progress={progress}
      onStarted={id => { setStarted(true); onStarted(id); }} onStopScan={onStop} notify={notify}
      onTab={next => { onTab(next); setTab(next); }} removeToast={vi.fn()} onTheme={vi.fn()} onErrors={vi.fn()} onHelp={vi.fn()} onAbout={onAbout} reading={reading} />
  );
}

const snapshot = (more: Partial<ScanProgressSnapshotDto> = {}): ScanProgressSnapshotDto => ({
  instanceId: 'scan-1', startedUtc: '2026-11-10T12:00:00Z', primaryTotal: 6941, primaryProcessed: 4165, secondaryTotal: 0, secondaryProcessed: 0,
  primaryPercent: 60, secondaryPercent: 0, overallPercent: 60, ...more,
});
const tabs = () => screen.queryByRole('navigation', { name: 'Tabs' });
const cover = (name: string) => screen.getByRole('region', { name });
const steps = () => Number(screen.getByRole('progressbar', { name: 'First steps' }).getAttribute('aria-valuenow'));

describe('first launch: Welcome and photo access', () => {
  it('welcomes the person and Get started says why PhotoSense needs the photos, in the platform words', async () => {
    render(<App />);
    const welcome = cover('Welcome');
    expect(within(welcome).getByRole('heading', { level: 1, name: 'PhotoSense' })).toBeInTheDocument();
    expect(welcome).toHaveTextContent('Free up space, back up to your own computer and keep your library tidy. Everything runs on your devices.');
    expect(within(welcome).getAllByRole('listitem').map(li => li.textContent)).toEqual([
      'Find duplicates, screenshots and blurry shots', 'Back up to your own computer over Wi-Fi', 'Delete only what is already backed up',
    ]);
    expect(steps()).toBe(1);
    expect(tabs()).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Get started' }));
    const why = cover('Photo access');
    expect(within(why).getByRole('heading', { name: 'Let PhotoSense see your photos' })).toBeInTheDocument();
    expect(why).toHaveTextContent('Photos are checked on this iPhone and never uploaded.');
    expect(why).toHaveTextContent('With limited access, PhotoSense can only check the photos you pick, so most of Clean up and Safe to delete will not work.');
    expect(steps()).toBe(2);
    expect(stage()).toBe('access');
  });

  it('allowed all, goes on to pairing; some, says so and goes on too', async () => {
    const notify = vi.fn();
    localStorage.setItem('photosense-first-run', 'access');
    const { unmount } = render(<App notify={notify} />);
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(cover('Connect your computer')).toBeInTheDocument();
    expect(stage()).toBe('pair');
    expect(notify).not.toHaveBeenCalled();
    unmount();

    localStorage.setItem('photosense-first-run', 'access');
    answer = 'selected';
    render(<App notify={notify} />);
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(notify).toHaveBeenCalledExactlyOnceWith('Limited access: PhotoSense checks only the photos you pick.', 'info');
    expect(cover('Connect your computer')).toBeInTheDocument();
  });

  it('refused, stays, saying so, and offers Settings; turned on there and back, goes on to pairing', async () => {
    const notify = vi.fn();
    localStorage.setItem('photosense-first-run', 'access');
    answer = 'nothing';
    const { rerender } = render(<App platform="android" notify={notify} />);
    expect(cover('Photo access')).toHaveTextContent('Photos are checked on this phone and never uploaded.');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(notify).toHaveBeenCalledExactlyOnceWith('PhotoSense needs photo access to work. You can allow it later in Settings.', 'info');
    expect(stage()).toBe('refused');
    await userEvent.click(screen.getByRole('button', { name: 'Open Settings' }));
    expect(api.openPhotoSettings).toHaveBeenCalledOnce();

    access = 'everything';
    rerender(<App platform="android" notify={notify} />);
    expect(cover('Connect your computer')).toBeInTheDocument();
    expect(stage()).toBe('pair');
  });

  it('says when the phone could not ask, or Settings could not be opened', async () => {
    const notify = vi.fn();
    localStorage.setItem('photosense-first-run', 'refused');
    access = 'nothing';
    api.askForPhotos.mockRejectedValueOnce(new Error('no window'));
    api.openPhotoSettings.mockRejectedValueOnce('denied');
    render(<App notify={notify} />);
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    await userEvent.click(screen.getByRole('button', { name: 'Open Settings' }));
    expect(notify.mock.calls).toEqual([['PhotoSense could not ask for your photos: no window', 'error'], ['Settings could not be opened: denied', 'error']]);
  });

  it('a phone that cannot remember starts again each time, and loses nothing by it', async () => {
    const throwing = { getItem: () => { throw new Error('no'); }, setItem: () => { throw new Error('no'); }, clear: () => undefined };
    vi.stubGlobal('localStorage', throwing);
    try {
      render(<App />);
      await userEvent.click(screen.getByRole('button', { name: 'Get started' }));
      // Accepted for this visit only, so it is asked again next time.
      await userEvent.click(within(cover('Keep a backup of photos you care about')).getByRole('button', { name: 'Accept and continue' }));
      expect(cover('Photo access')).toBeInTheDocument();
    } finally { vi.unstubAllGlobals(); }
  });
});

describe('the note asking for a backup', () => {
  const NOTE = 'Keep a backup of photos you care about';
  beforeEach(() => localStorage.clear());

  it('comes after Welcome, reads the terms of use over it, and is put away only by accepting it, which is remembered', async () => {
    const onAbout = vi.fn();
    const view = render(<App onAbout={onAbout} />);
    await userEvent.click(screen.getByRole('button', { name: 'Get started' }));
    const note = cover(NOTE);
    expect(note).toHaveTextContent('PhotoSense asks before it removes or moves anything, and what it removes can be got back for a while. Even so, a mistake can lose a photo or video.');
    expect(note).toHaveTextContent('Before you use PhotoSense, keep a backup of the photos and videos you care about, and keep it up to date.');
    expect(note).toHaveTextContent('Using PhotoSense means you accept its terms of use.');
    await userEvent.click(within(note).getByRole('button', { name: 'Read the terms of use' }));
    expect(onAbout).toHaveBeenCalledWith('terms');
    // While About shows the terms the note waits, hidden, and still nothing behind it starts.
    view.rerender(<App onAbout={onAbout} reading />);
    expect(screen.queryByRole('region', { name: NOTE })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Photo access' })).not.toBeInTheDocument();
    expect(api.libraryRoot).not.toHaveBeenCalled();
    view.rerender(<App onAbout={onAbout} />);
    fireEvent.keyDown(window, { key: 'Escape' });
    await userEvent.click(within(cover(NOTE)).getByRole('button', { name: 'Accept and continue' }));
    expect(localStorage.getItem('photosense-backup-note')).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(cover('Photo access')).toBeInTheDocument();
  });

  it('comes at the next launch on a phone past its first launch that has not accepted it, and then the tabs', async () => {
    localStorage.setItem('photosense-first-run', 'done');
    access = 'everything';
    render(<App />);
    expect(screen.queryByRole('navigation', { name: 'Tabs' })).not.toBeInTheDocument();
    await userEvent.click(within(cover(NOTE)).getByRole('button', { name: 'Accept and continue' }));
    expect(screen.queryByRole('region', { name: NOTE })).not.toBeInTheDocument();
  });
});

describe('first launch: pairing a computer', () => {
  beforeEach(() => localStorage.setItem('photosense-first-run', 'pair'));

  it('says how, and Scan the code pairs, then says where backups go; Continue starts the first scan', async () => {
    const started = vi.fn();
    render(<App onStarted={started} />);
    const pair = cover('Connect your computer');
    expect(pair).toHaveTextContent('Backups and Safe to delete need PhotoSense on your Mac or PC. Both need to be on the same Wi-Fi.');
    expect(within(pair).getAllByRole('listitem').map(li => li.textContent)).toEqual([
      '1Install PhotoSense on your Mac or PC.', '2On the computer, open Phones and choose Pair a phone.', '3Point this iPhone at the code on the screen.',
    ]);
    expect(steps()).toBe(3);

    await userEvent.click(screen.getByRole('button', { name: 'Scan the code' }));
    const paired = cover('Paired');
    expect(within(paired).getByRole('heading', { name: 'Paired with Studio iMac' })).toBeInTheDocument();
    expect(paired).toHaveTextContent('Backups go to Pictures/PhotoSense on Studio iMac. You can change the folder on the computer.');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(stage()).toBe('scan');
    await vi.waitFor(() => expect(started).toHaveBeenCalledWith('scan-1'));
    expect(loadLastScan()).toEqual({ root: '/Photo Library', second: '', recursive: true });
  });

  it('takes the code typed instead, says what it is doing, and asks whether the numbers match', async () => {
    const { rerender } = render(<App />);
    await userEvent.click(screen.getByRole('button', { name: 'Enter the code instead' }));
    const box = screen.getByRole('textbox', { name: 'Code from the computer' });
    expect(screen.getByRole('button', { name: 'Pair' })).toBeDisabled();
    await userEvent.type(box, '482 917');
    await userEvent.click(screen.getByRole('button', { name: 'Pair' }));
    expect(api.enterPairingCode).toHaveBeenCalledExactlyOnceWith('482 917');
    expect(screen.getByRole('status')).toHaveTextContent('Looking for the computer showing that code…');
    expect(screen.getByRole('button', { name: 'Pair' })).toBeDisabled();

    pairing = pairingAt({ stage: 'connecting', other: 'Studio iMac' });
    rerender(<App />);
    expect(screen.getByRole('status')).toHaveTextContent('Connecting to Studio iMac…');
    pairing = pairingAt({ stage: 'comparing', other: 'Studio iMac', check: '271 004' });
    rerender(<App />);
    const compare = cover('Compare the numbers');
    expect(compare).toHaveTextContent('Studio iMac shows a number. Pair only if it is the same as this one.');
    expect(within(compare).getByLabelText('The number')).toHaveTextContent('271 004');
    await userEvent.click(screen.getByRole('button', { name: "It's different" }));
    expect(api.confirmPairing).toHaveBeenLastCalledWith(false);
    expect(screen.getByRole('alert')).toHaveTextContent('The numbers did not match.');

    await userEvent.click(screen.getByRole('button', { name: 'Scan the code instead' }));
    expect(screen.getByRole('button', { name: 'Scan the code' })).toBeInTheDocument();
    pairing = pairingAt({ stage: 'comparing', other: 'Studio iMac', check: '271 004' });
    rerender(<App />);
    await userEvent.click(screen.getByRole('button', { name: "It's the same number" }));
    expect(api.confirmPairing).toHaveBeenLastCalledWith(true);
    expect(cover('Paired')).toBeInTheDocument();
  });

  it('Skip for now stops pairing and starts the first scan; a failure to pair is said', async () => {
    const notify = vi.fn();
    api.stopPairing.mockRejectedValueOnce(new Error('gone'));
    api.scanPairing.mockRejectedValueOnce(new Error('The camera is in use.'));
    render(<App notify={notify} />);
    await userEvent.click(screen.getByRole('button', { name: 'Scan the code' }));
    expect(notify).toHaveBeenCalledExactlyOnceWith('The camera is in use.', 'error');
    await userEvent.click(screen.getByRole('button', { name: 'Skip for now' }));
    expect(api.stopPairing).toHaveBeenCalledOnce();
    expect(stage()).toBe('scan');
    expect(cover('Scanning')).toBeInTheDocument();
  });

  it('says it when the first scan could not start', async () => {
    const notify = vi.fn();
    api.startScan.mockRejectedValueOnce(new Error('busy'));
    render(<App notify={notify} />);
    await userEvent.click(screen.getByRole('button', { name: 'Skip for now' }));
    await vi.waitFor(() => expect(notify).toHaveBeenCalledWith('The scan could not start: busy', 'error'));
  });
});

describe('first launch: the first scan and what it found', () => {
  it('counts the photos read and what has been found so far, says nothing leaves the phone, and ends in what it found', async () => {
    localStorage.setItem('photosense-first-run', 'scan');
    const stop = vi.fn();
    const { rerender } = render(<App scanning progress={snapshot()} onStop={stop} />);
    const scan = cover('Scanning');
    expect(scan).toHaveTextContent('Scanning your library4,165of 6,941 photos and videos');
    expect(screen.getByRole('progressbar', { name: 'Photos read' })).toHaveAttribute('aria-valuenow', '60');
    expect([...scan.querySelectorAll('dl > div')].map(d => d.textContent)).toEqual(['Duplicates264', 'Similar shots512', 'Large videos0']);
    expect(scan).toHaveTextContent('Everything stays on this iPhone.');
    expect(within(scan).getByRole('status')).toHaveTextContent('Scanning…');
    expect(screen.queryByRole('button', { name: 'Scanning…' })).not.toBeInTheDocument();
    expect(steps()).toBe(4);
    await userEvent.click(screen.getByRole('button', { name: 'Stop' }));
    expect(stop).toHaveBeenCalledOnce();

    rerender(<App scanning={false} />);
    const result = cover('Scan complete');
    expect(result).toHaveTextContent('Scan complete5.18 GBcan be freed from this iPhone');
    expect([...result.querySelectorAll('dl > div')].map(d => d.textContent)).toEqual(['Similar shots5123.32 GB', 'Duplicates2641.86 GB']);
    expect(result).toHaveTextContent('The free plan removes 50 items a day, and Swipe to clean is included. Nothing is deleted until you confirm.');
    expect(stage()).toBe('result');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(cover('PhotoSense Pro')).toBeInTheDocument();
    expect(stage()).toBe('offer');
  });

  it('counts before it knows how many there are, with nothing found yet, on a tablet', () => {
    localStorage.setItem('photosense-first-run', 'scan');
    found = undefined;
    width(true);
    render(<App platform="android" scanning progress={snapshot({ primaryTotal: 0, primaryProcessed: 0 })} />);
    expect(cover('Scanning')).toHaveTextContent('Counting your photos and videos…');
    expect(cover('Scanning')).toHaveTextContent('Everything stays on this tablet.');
    expect(screen.getByRole('progressbar', { name: 'Photos read' })).toHaveAttribute('aria-valuenow', '0');
  });

  it('a scan after the first shows no steps, and the tabs come back once it ends', () => {
    localStorage.setItem('photosense-first-run', 'done');
    access = 'everything';
    const { rerender } = render(<App scanning />);
    expect(cover('Scanning')).toBeInTheDocument();
    expect(screen.queryByRole('progressbar', { name: 'First steps' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Stop' })).toBeInTheDocument();
    rerender(<App />);
    expect(screen.queryByRole('region', { name: 'Scanning' })).not.toBeInTheDocument();
  });

  it('on Pro, or with nothing to free, goes straight to Clean up', async () => {
    const onTab = vi.fn();
    localStorage.setItem('photosense-first-run', 'result');
    currentPlan = plan({ tier: 'pro', kind: 'lifetime', leftToday: null });
    const { unmount } = render(<App onTab={onTab} />);
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(onTab).toHaveBeenLastCalledWith('cleanup');
    expect(stage()).toBe('done');
    unmount();

    // On the free plan with nothing to free, there is nothing to offer either.
    localStorage.setItem('photosense-first-run', 'result');
    currentPlan = plan();
    found = { items: [{ kind: 'duplicates', count: 0, bytes: 0 }] };
    render(<App onTab={onTab} />);
    expect(cover('Scan complete')).toHaveTextContent('Nothingcan be freed from this iPhone');
    expect(cover('Scan complete')).toHaveTextContent('The free plan removes 50 items a day');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(stage()).toBe('done');
  });

  it('waits for what was found before Continue, and says the usual 50 before the plan is known, on an iPad too', () => {
    localStorage.setItem('photosense-first-run', 'result');
    found = undefined;
    currentPlan = undefined;
    width(true);
    render(<App />);
    expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled();
    expect(cover('Scan complete')).toHaveTextContent('can be freed from this iPad');
    expect(cover('Scan complete')).toHaveTextContent('The free plan removes 50 items a day');
  });

  it('offers no plan before the store has been asked', () => {
    localStorage.setItem('photosense-first-run', 'offer');
    currentPlan = undefined;
    render(<App />);
    expect(screen.queryByRole('radio')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Continue with Free' })).toBeEnabled();
  });
});

describe('first launch: the one-time offer', () => {
  beforeEach(() => localStorage.setItem('photosense-first-run', 'offer'));

  it('offers to free all of it with Pro, backing up to the computer paired, at the store prices; Continue with Free ends on Clean up', async () => {
    const onTab = vi.fn();
    pairing = pairingAt({ stage: 'paired', other: 'Studio iMac' });
    render(<App onTab={onTab} />);
    const offer = cover('PhotoSense Pro');
    expect(within(offer).getByRole('heading', { name: 'Free all 5.18 GB with Pro' })).toBeInTheDocument();
    expect(within(offer).getAllByRole('listitem').map(li => li.textContent)).toEqual([
      'Unlimited removals every day', 'Automatic backup to Studio iMac', 'Video compression', 'Search, fix dates and the hidden vault',
    ]);
    expect(within(offer).getAllByRole('radio')).toHaveLength(3);
    expect(offer).toHaveTextContent('Free for 7 days, then $19.99 a year.');
    await userEvent.click(screen.getByRole('button', { name: 'Continue with Free' }));
    expect(onTab).toHaveBeenLastCalledWith('cleanup');
    expect(stage()).toBe('done');
  });

  it('buys the plan chosen, carrying on to Clean up; waits for approval; says what went wrong; Not now ends it too', async () => {
    const notify = vi.fn(), onTab = vi.fn();
    const buying = deferred<{ outcome: string; plan: PlanDto }>();
    api.buyPlan.mockReturnValueOnce(buying.promise);
    const { unmount } = render(<App notify={notify} onTab={onTab} />);
    expect(cover('PhotoSense Pro')).toHaveTextContent('Automatic backup to your computer');
    await userEvent.click(screen.getByRole('radio', { name: /^Monthly/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Subscribe for $2.99 a month' }));
    expect(screen.getByRole('button', { name: 'Continue with Free' })).toBeDisabled();
    await act(async () => buying.resolve({ outcome: 'cancelled', plan: plan() }));
    expect(onTab).not.toHaveBeenCalled();
    api.buyPlan.mockRejectedValueOnce(new Error('The App Store could not be reached. Try again.'));
    await userEvent.click(screen.getByRole('button', { name: 'Subscribe for $2.99 a month' }));
    expect(notify).toHaveBeenLastCalledWith('The App Store could not be reached. Try again.', 'error');
    api.buyPlan.mockResolvedValueOnce({ outcome: 'bought', plan: plan({ tier: 'pro' }) });
    await userEvent.click(screen.getByRole('button', { name: 'Subscribe for $2.99 a month' }));
    expect(notify).toHaveBeenLastCalledWith('PhotoSense Pro is on.', 'ok');
    expect(onTab).toHaveBeenLastCalledWith('cleanup');
    unmount();

    localStorage.setItem('photosense-first-run', 'offer');
    api.buyPlan.mockResolvedValueOnce({ outcome: 'pending', plan: plan() });
    const second = render(<App notify={notify} onTab={onTab} />);
    await userEvent.click(screen.getByRole('button', { name: 'Start 7-day free trial' }));
    expect(notify).toHaveBeenLastCalledWith('The purchase is waiting for approval. Pro turns on once it is approved.', 'info');
    expect(stage()).toBe('done');
    second.unmount();

    localStorage.setItem('photosense-first-run', 'offer');
    currentPlan = plan({ offers: [] });
    found = undefined;
    render(<App onTab={onTab} />);
    expect(screen.queryByRole('radio')).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Free it all with Pro' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Not now' }));
    expect(stage()).toBe('done');
  });
});
