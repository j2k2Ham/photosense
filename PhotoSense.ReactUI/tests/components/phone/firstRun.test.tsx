import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PhoneApp } from '../../../components/phone/PhoneApp';
import { loadLastScan } from '../../../lib/lastScan';
import type { Platform, TabId } from '../../../lib/phone';
import type { ScanProgressSnapshotDto } from '../../../types';
import { deferred } from '../../deferred';
import { width } from './frame';

const api = vi.hoisted(() => ({ usePhotoAccess: vi.fn(), askForPhotos: vi.fn(), openPhotoSettings: vi.fn(), libraryRoot: vi.fn(), startScan: vi.fn(), useGroupsThrough: vi.fn() }));
vi.mock('../../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/apiClient')>()), ...api }));

type Access = 'everything' | 'selected' | 'nothing';
/** What the phone lets PhotoSense see now; undefined until it has said. */
let access: Access | undefined;
/** What the person answers to the phone's question. */
let answer: Access;
const stage = () => localStorage.getItem('photosense-first-run');

beforeEach(() => {
  for (const mock of Object.values(api)) mock.mockReset();
  access = undefined;
  answer = 'everything';
  api.usePhotoAccess.mockImplementation(() => ({ data: access && { access } }));
  api.askForPhotos.mockImplementation(async () => ({ access: (access = answer) }));
  api.openPhotoSettings.mockResolvedValue(undefined);
  api.libraryRoot.mockResolvedValue('/Photo Library');
  api.startScan.mockResolvedValue({ instanceId: 'scan-1' });
  api.useGroupsThrough.mockReturnValue({});
  width(false);
});

interface AppProps {
  platform?: Platform;
  scanning?: boolean;
  progress?: ScanProgressSnapshotDto;
  onStarted?(id: string): void;
  onStop?(): void;
  notify?(message: string, kind: string): void;
}

/** The phone layout, with what the page would give it. */
function App({ platform = 'ios', scanning = false, progress, onStarted = vi.fn(), onStop = vi.fn(), notify = vi.fn() }: AppProps) {
  const [tab, setTab] = React.useState<TabId>('cleanup');
  return (
    <PhoneApp platform={platform} tab={tab}
      toasts={[]} theme="dark" errorCount={0} settings={{ eraseAskTwice: true, distanceUnit: 'miles' }} onSettings={vi.fn()} scanning={scanning} progress={progress} onStarted={onStarted} onStopScan={onStop} notify={notify}
      onTab={setTab} removeToast={vi.fn()} onTheme={vi.fn()} onErrors={vi.fn()} onHelp={vi.fn()} onAbout={vi.fn()} />
  );
}

const snapshot = (more: Partial<ScanProgressSnapshotDto> = {}): ScanProgressSnapshotDto => ({
  instanceId: 'scan-1', startedUtc: '2026-11-10T12:00:00Z', primaryTotal: 0, primaryProcessed: 0, secondaryTotal: 0, secondaryProcessed: 0,
  primaryPercent: 100, secondaryPercent: 100, overallPercent: 100, ...more,
});
const tabs = () => screen.queryByRole('navigation', { name: 'Tabs' });

describe('the first run on a phone', () => {
  it('welcomes the person in the platform\'s words, and Continue asks for the photos and scans the whole library', async () => {
    const started = vi.fn();
    render(<App platform="android" onStarted={started} />);
    const welcome = screen.getByRole('region', { name: 'Welcome' });
    expect(within(welcome).getByRole('heading', { level: 1, name: 'Welcome to PhotoSense' })).toBeInTheDocument();
    expect(welcome).toHaveTextContent('Suggested folders for each place and year, with a preview before anything moves.');
    expect(welcome).toHaveTextContent('Runs on this phone. Nothing is uploaded.');
    expect(tabs()).not.toBeInTheDocument();
    api.libraryRoot.mockResolvedValue('/Phone');

    await userEvent.click(within(welcome).getByRole('button', { name: 'Continue' }));

    expect(api.askForPhotos).toHaveBeenCalledOnce();
    expect(api.startScan).toHaveBeenCalledWith({ primaryLocation: '/Phone', recursive: true });
    expect(started).toHaveBeenCalledWith('scan-1');
    expect([stage(), loadLastScan().root]).toEqual(['done', '/Phone']);
    // The page is told of the scan, and shows it; until then the tabs are there.
    expect(tabs()).toBeInTheDocument();
  });

  it('when refused, says where access is turned on, asks again, and scans once access is on and the person is back', async () => {
    const notify = vi.fn();
    answer = 'nothing';
    const view = render(<App notify={notify} />);
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    const refused = screen.getByRole('region', { name: 'Photo access' });
    expect(within(refused).getByRole('heading', { name: 'PhotoSense can\'t see your photos' })).toBeInTheDocument();
    expect(refused).toHaveTextContent('Turn on access in Settings › Apps › PhotoSense › Photos.');
    expect(stage()).toBe('refused');

    await userEvent.click(within(refused).getByRole('button', { name: 'Ask again' }));
    expect(notify).toHaveBeenCalledWith('PhotoSense still can\'t see your photos. Turn on access in Settings.', 'info');
    await userEvent.click(within(refused).getByRole('button', { name: 'Open Settings' }));
    expect(api.openPhotoSettings).toHaveBeenCalledOnce();
    expect(api.startScan).not.toHaveBeenCalled();

    // Back from Settings with access on: the first scan starts, once, however often the screen is drawn meanwhile.
    const starting = deferred<{ instanceId: string }>();
    api.startScan.mockReturnValue(starting.promise);
    access = 'everything';
    const started = vi.fn();
    view.rerender(<App notify={notify} onStarted={started} />);
    expect(await screen.findByRole('heading', { name: 'Reading your photos' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Stop' })).toBeDisabled();
    view.rerender(<App notify={notify} onStarted={vi.fn()} />);
    await act(async () => starting.resolve({ instanceId: 'scan-2' }));
    expect(api.startScan).toHaveBeenCalledOnce();
    expect(stage()).toBe('done');
    expect(tabs()).toBeInTheDocument();
  });

  it('on Android says where its permissions are, and Ask again allowed starts the scan as coming back does', async () => {
    localStorage.setItem('photosense-first-run', 'refused');
    access = 'nothing';
    render(<App platform="android" />);
    expect(screen.getByRole('region', { name: 'Photo access' })).toHaveTextContent('Turn on access in Settings › Apps › PhotoSense › Permissions.');
    await userEvent.click(screen.getByRole('button', { name: 'Ask again' }));
    expect(api.startScan).toHaveBeenCalledOnce();
    expect(stage()).toBe('done');
  });

  it('waits, showing nothing that might be wrong, until the phone has said what PhotoSense may see', () => {
    localStorage.setItem('photosense-first-run', 'done');
    const { container } = render(<App />);
    expect(container.ownerDocument.querySelector('[aria-busy="true"]')).toBeInTheDocument();
    expect(tabs()).not.toBeInTheDocument();
  });

  it('says when the phone could not ask, a scan could not start, or Settings could not be opened', async () => {
    const notify = vi.fn();
    api.askForPhotos.mockRejectedValue(new Error('No one to ask.'));
    const view = render(<App notify={notify} />);
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(notify).toHaveBeenLastCalledWith('PhotoSense could not ask for your photos: No one to ask.', 'error');
    expect(screen.getByRole('region', { name: 'Welcome' })).toBeInTheDocument();

    localStorage.setItem('photosense-first-run', 'done');
    access = 'nothing';
    api.openPhotoSettings.mockRejectedValue('no settings');
    view.unmount();
    render(<App notify={notify} />);
    await userEvent.click(screen.getByRole('button', { name: 'Open Settings' }));
    expect(notify).toHaveBeenLastCalledWith('Settings could not be opened: no settings', 'error');
  });

  it('a phone that cannot remember is welcomed each time, and loses nothing by it', async () => {
    const refusing = () => { throw new Error('storage is off'); };
    vi.stubGlobal('localStorage', { getItem: refusing, setItem: refusing, clear() {} });
    const started = vi.fn();
    render(<App onStarted={started} />);
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(started).toHaveBeenCalledWith('scan-1');
  });
});

describe('scanning on a phone', () => {
  beforeEach(() => {
    localStorage.setItem('photosense-first-run', 'done');
    access = 'everything';
  });

  it('counts first, then says how far it has got and how long is left, its tiles filling with the pictures read, and Stop stops it', async () => {
    const stop = vi.fn();
    const view = render(<App scanning progress={snapshot({ overallPercent: 0 })} onStop={stop} />);
    const scanning = screen.getByRole('region', { name: 'Scanning' });
    expect(scanning).toHaveTextContent('0%');
    expect(scanning).toHaveTextContent('Counting your photos and videos…');
    expect(tabs()).not.toBeInTheDocument();

    const far = snapshot({ primaryTotal: 6941, primaryProcessed: 2638, overallPercent: 38.006, secondsLeft: 240, recent: ['p1', 'p2'] });
    view.rerender(<App scanning progress={far} onStop={stop} />);
    view.rerender(<App scanning progress={{ ...far, recent: ['p2', 'p3'] }} onStop={stop} />);
    expect(scanning).toHaveTextContent('38%About 4 minutes left');
    expect(scanning).toHaveTextContent('Read 2,638 of 6,941 photos and videos');
    expect(within(scanning).getByRole('progressbar', { name: 'Photos read' })).toHaveAttribute('aria-valuenow', '38');
    const tiles = scanning.querySelector('[aria-hidden="true"].grid')!.children;
    expect(tiles).toHaveLength(80);
    const pictures = [...tiles].filter(t => t.tagName === 'IMG').map(t => t.getAttribute('src'));
    expect(pictures).toEqual(['p1', 'p2', 'p3'].map(id => `http://localhost:7071/api/photos/${id}/thumbnail`));
    // 38% of 80 tiles: 30 lit, three of them with a picture read, the rest in the accent.
    expect([...tiles].filter(t => t.className.includes('bg-acc')).length).toBe(27);

    await userEvent.click(within(scanning).getByRole('button', { name: 'Stop' }));
    expect(stop).toHaveBeenCalledOnce();

    // A scan just started again has nothing to say yet.
    view.rerender(<App scanning onStop={stop} />);
    expect(scanning).toHaveTextContent('Working out how long this will take');
  });

  it('has more tiles on a tablet, sixteen to a row', () => {
    width(true);
    render(<App scanning progress={snapshot()} />);
    const grid = screen.getByRole('region', { name: 'Scanning' }).querySelector('[aria-hidden="true"].grid')!;
    expect([grid.children.length, grid.className.includes('repeat(16')]).toEqual([96, true]);
  });

  it('starts again from Settings, and says when it could not', async () => {
    const notify = vi.fn();
    api.libraryRoot.mockRejectedValue(new Error('The phone did not say where its photos are.'));
    render(<App notify={notify} />);
    await userEvent.click(screen.getAllByRole('button', { name: 'Settings' })[0]);
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Settings' })).getByRole('button', { name: 'Scan again' }));
    expect(notify).toHaveBeenCalledWith('The scan could not start: The phone did not say where its photos are.', 'error');
    expect(api.startScan).not.toHaveBeenCalled();
  });
});
