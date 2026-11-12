import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PhoneApp } from '../../../components/phone/PhoneApp';
import { PhoneShell } from '../../../components/phone/PhoneShell';
import { PushedHeader, PushedScreen, RootHeader, Screen, SplitView } from '../../../components/phone/Screens';
import { DeletePermanently, SettingsSheet } from '../../../components/phone/SettingsSheet';
import type { Platform, TabId } from '../../../lib/phone';
import type { AppSettings } from '../../../lib/settings';
import { frame, inFrame, width } from './frame';
import { group, groupsPage, photo } from './groups';

// The phone has said PhotoSense may see everything; what comes before that is tested with the first run. Clean up,
// tested on its own, has one group of duplicates.
const api = vi.hoisted(() => ({ usePhotoAccess: vi.fn(), useGroupsThrough: vi.fn(), libraryRoot: vi.fn(), startScan: vi.fn(), useOrganizeBatches: vi.fn(() => ({})), useSendState: vi.fn(() => ({})),
  useRemoved: vi.fn<(roots: string[] | null) => { data?: unknown }>(() => ({})), eraseRemoved: vi.fn(), openPhotoSettings: vi.fn(), undoChange: vi.fn(),
}));
vi.mock('../../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/apiClient')>()), ...api }));
beforeEach(() => {
  api.usePhotoAccess.mockReturnValue({ data: { access: 'everything' } });
  api.useGroupsThrough.mockReturnValue({ data: groupsPage('duplicates', [group(photo('IMG_4299.JPG'), [photo('IMG_4299.HEIC')])]) });
});
// Library and Organize are tested on their own; here the library holds two photos, one taken in Anaconda and one with no place.
vi.mock('../../../lib/library', async importOriginal => {
  const real = await importOriginal<typeof import('../../../lib/library')>();
  const photo = (id: string, place?: number) => ({ id, name: `${id}.JPG`, folder: '/Photo Library', sizeBytes: 1, isVideo: false, date: '2023-12-01T00:00:00', dateFromFile: false, width: 1, height: 1, place });
  const files = [photo('IMG_1', 0), photo('IMG_2')];
  const places = [{ town: 'Anaconda', state: 'Montana', country: 'US', latitude: 46.1, longitude: -112.9 }];
  return { ...real, useLibrary: () => ({ root: '/Photo Library', files, listing: { root: '/Photo Library', fromScan: true, places, files } }) };
});

describe('a screen\'s header', () => {
  it('on an iPhone: round glass buttons over a large title, and the gear opens Settings', async () => {
    const given = frame();
    const select = vi.fn();
    inFrame(<RootHeader title="Library" subtitle="6,941 photos and videos" textAction={{ label: 'Select', run: select }} buttons={<span>map</span>} />, given);
    expect(screen.getByRole('heading', { level: 1, name: 'Library' })).toHaveClass('text-[length:var(--h1)]');
    expect(screen.getByText('6,941 photos and videos')).toBeInTheDocument();
    expect(screen.getByRole('banner')).toHaveClass('pt-[var(--safe-t)]');
    await userEvent.click(screen.getByRole('button', { name: 'Select' }));
    await userEvent.click(screen.getByRole('button', { name: 'Settings' }));
    expect([select.mock.calls.length, (given.openSettings as ReturnType<typeof vi.fn>).mock.calls.length]).toEqual([1, 1]);
    expect(screen.getByText('map')).toBeInTheDocument();
  });

  it('on an iPad with its sidebar hidden starts lower, under the floating tabs; on a tablet\'s list it has no gear', () => {
    inFrame(<RootHeader title="Clean up" noGear />, frame('ios', { tablet: true, sidebarHidden: true }));
    expect(screen.getByRole('banner')).toHaveClass('pt-[calc(var(--safe-t)+62px)]');
    expect(screen.queryByRole('button', { name: 'Settings' })).not.toBeInTheDocument();
  });

  it('on Android: a bar with the title on the left, a cross to stop selecting, and the subtitle under it', async () => {
    const stop = vi.fn();
    const { again } = inFrame(<RootHeader title="3 selected" onStop={stop} />, frame('android'));
    await userEvent.click(screen.getByRole('button', { name: 'Stop selecting' }));
    expect(stop).toHaveBeenCalledOnce();
    expect(screen.queryByText('6,941 photos and videos')).not.toBeInTheDocument();
    again(<RootHeader title="Library" subtitle="6,941 photos and videos" textAction={{ label: 'Select', run: vi.fn() }} noGear />, frame('android'));
    expect(screen.getByText('6,941 photos and videos')).toHaveClass('px-4');
    expect(screen.queryByRole('button', { name: 'Settings' })).not.toBeInTheDocument();
  });

  it('of a pushed screen: back, and the title in the middle on an iPhone or beside the arrow on Android', async () => {
    const back = vi.fn();
    const { again } = inFrame(<PushedHeader title="IMG_4299.JPG" subtitle="1 duplicate" onBack={back} />);
    expect(screen.getByRole('heading', { name: 'IMG_4299.JPG' }).parentElement).toHaveClass('text-center');
    await userEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(back).toHaveBeenCalledOnce();
    again(<PushedHeader title="Anaconda" onBack={back} />, frame('android'));
    expect(screen.getByRole('heading', { name: 'Anaconda' })).toHaveClass('text-[22px]', 'font-normal');
    expect(screen.getByRole('button', { name: 'Back' })).toBeInTheDocument();
    expect(screen.getByRole('banner').querySelector('p')).toBeNull();
    // Android says the subtitle under the bar.
    again(<PushedHeader title="Anaconda" subtitle="Preview. Nothing moves until you tap Move." onBack={back} />, frame('android'));
    expect(screen.getByText('Preview. Nothing moves until you tap Move.')).toHaveClass('px-4', 'pb-3');
  });

  it('of a tablet\'s detail: no back, the title on the left, and the gear', () => {
    const { again } = inFrame(<PushedHeader title="IMG_4299.JPG" gear />, frame('ios', { tablet: true }));
    expect(screen.queryByRole('button', { name: 'Back' })).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'IMG_4299.JPG' })).toHaveClass('text-[22px]', 'font-bold');
    expect(screen.getByRole('button', { name: 'Settings' })).toBeInTheDocument();
    again(<PushedHeader title="IMG_4299.JPG" onBack={vi.fn()} gear />, frame('ios', { tablet: true }));
    expect(screen.getAllByRole('button')).toHaveLength(2);
    again(<PushedHeader title="Anaconda" gear />, frame('android', { tablet: true }));
    expect(screen.getByRole('button', { name: 'Settings' })).toBeInTheDocument();
  });
});

describe('a screen', () => {
  it('leaves room at its foot for the bar there, and none for a pushed screen', () => {
    const { again } = inFrame(<Screen label="Library">x</Screen>);
    expect(screen.getByRole('region', { name: 'Library' })).toHaveClass('pb-[120px]', 'overflow-y-auto');
    again(<Screen label="Library" fixed>x</Screen>, frame('android'));
    expect(screen.getByRole('region', { name: 'Library' })).toHaveClass('pb-[104px]', 'overflow-hidden');
    again(<Screen label="Library">x</Screen>, frame('ios', { foot: 'none' }));
    expect(screen.getByRole('region', { name: 'Library' }).className).not.toMatch(/pb-/);
    again(<Screen label="Library">x</Screen>, frame('ios', { tablet: true }));
    expect(screen.getByRole('region', { name: 'Library' })).toHaveClass('pb-10');
  });

  it('pushed, slides in; on a tablet, the list stands beside the detail', () => {
    const { again } = inFrame(<PushedScreen label="Review">x</PushedScreen>);
    expect(screen.getByRole('region', { name: 'Review' })).toHaveClass('animate-push-in');
    again(<SplitView list={<p>the list</p>} detail={<p>the detail</p>} />, frame('ios', { tablet: true }));
    expect(screen.getByText('the list').parentElement).toHaveClass('w-[360px]');
    again(<SplitView list={<p>the list</p>} detail={<p>the detail</p>} />, frame('android', { tablet: true }));
    expect(screen.getByText('the list').parentElement).toHaveClass('w-[400px]', 'border-sep');
    expect(screen.getByText('the detail')).toBeInTheDocument();
  });
});

describe('the shell', () => {
  const screens = { library: <p>library screen</p>, cleanup: <p>cleanup screen</p>, organize: <p>organize screen</p>, send: <p>send screen</p> };
  function Shell({ platform = 'ios', start = 'library', foot = 'tabs' as const }: { platform?: Platform; start?: TabId; foot?: 'tabs' | 'selection' | 'none' }) {
    const [tab, setTab] = React.useState<TabId>(start);
    return <PhoneShell platform={platform} tab={tab} foot={foot} counts={{ cleanup: '264' }} screens={screens} toasts={[]} onTab={setTab} onSettings={vi.fn()} removeToast={vi.fn()}
      notice={<p>the notice</p>} sheet={<p>the sheet</p>} />;
  }

  it('on an iPhone: the tabs in a floating glass bar, each tab kept as it was once turned to', async () => {
    width(false);
    render(<Shell />);
    const tabs = screen.getByRole('navigation', { name: 'Tabs' });
    expect(tabs).toHaveClass('bottom-[22px]', 'rounded-full');
    expect(within(tabs).getAllByRole('button').map(b => b.textContent)).toEqual(['Library', 'Clean up', 'Organize', 'Send']);
    expect(within(tabs).getByRole('button', { name: 'Library' })).toHaveAttribute('aria-current', 'page');
    expect(screen.queryByText('cleanup screen')).not.toBeInTheDocument();
    await userEvent.click(within(tabs).getByRole('button', { name: 'Clean up' }));
    expect(screen.getByText('cleanup screen').parentElement).not.toHaveClass('hidden');
    expect(screen.getByText('library screen').parentElement).toHaveClass('hidden');
    expect(screen.getByText('the notice')).toBeInTheDocument();
    expect(screen.getByText('the sheet')).toBeInTheDocument();
  });

  it('on Android: a navigation bar across the foot, and the page painting under the phone\'s own bars', () => {
    width(false);
    const { container, rerender } = render(<Shell platform="android" />);
    expect(screen.getByRole('navigation', { name: 'Tabs' })).toHaveClass('h-20', 'bottom-[var(--safe-b)]');
    expect(within(screen.getByRole('navigation')).getByRole('button', { name: 'Library' }).firstElementChild).toHaveClass('bg-bar-hi');
    const strips = container.querySelectorAll('[aria-hidden="true"].fixed');
    expect(strips[1]).toHaveClass('bg-bar');
    rerender(<Shell platform="android" foot="none" />);
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
    expect(container.querySelectorAll('[aria-hidden="true"].fixed')[1]).toHaveClass('bg-bg');
  });

  it('on an iPad: a sidebar with how many each tab holds, which hides behind a floating bar of tabs', async () => {
    width(true);
    render(<Shell start="cleanup" />);
    const side = screen.getByRole('navigation', { name: 'Tabs' });
    expect(within(side).getByText('PhotoSense')).toBeInTheDocument();
    expect(within(side).getByRole('button', { name: 'Clean up 264' })).toHaveAttribute('aria-current', 'page');
    expect(within(side).getByText('Runs on this iPad. Nothing is uploaded.')).toBeInTheDocument();
    await userEvent.click(within(side).getByRole('button', { name: 'Library' }));
    expect(within(side).getByRole('button', { name: 'Library' })).toHaveAttribute('aria-current', 'page');
    await userEvent.click(within(side).getByRole('button', { name: 'Hide sidebar' }));
    const floating = screen.getByRole('navigation', { name: 'Tabs' });
    expect(floating).toHaveClass('-translate-x-1/2');
    await userEvent.click(within(floating).getByRole('button', { name: 'Send' }));
    expect(within(floating).getByRole('button', { name: 'Send' })).toHaveAttribute('aria-current', 'page');
    await userEvent.click(screen.getByRole('button', { name: 'Show sidebar' }));
    expect(screen.getByRole('button', { name: 'Hide sidebar' })).toBeInTheDocument();
  });

  it('on an Android tablet: a rail down the left', async () => {
    width(true);
    const { container } = render(<Shell platform="android" />);
    const rail = screen.getByRole('navigation', { name: 'Tabs' });
    expect(rail).toHaveClass('w-24', 'bg-rail');
    await userEvent.click(within(rail).getByRole('button', { name: 'Organize' }));
    expect(within(rail).getByRole('button', { name: 'Organize' })).toHaveAttribute('aria-current', 'page');
    expect(container.querySelectorAll('[aria-hidden="true"].fixed')[1]).toHaveClass('bg-bg');
  });
});

describe('Settings', () => {
  const removed = (files: number, bytes = 212 * 1024 ** 2) => ({ files, bytes, folders: files ? [{ path: '/Photo Library/_PhotoSense_Removed', files, bytes }] : [] });
  function show(platform: Platform = 'ios', more = {}) {
    const calls = {
      onTheme: vi.fn(), onUnit: vi.fn(), onPhotoAccess: vi.fn(), onScanAgain: vi.fn(), onDeletePermanently: vi.fn(), onRecent: vi.fn(), onErrors: vi.fn(), onHelp: vi.fn(),
      onAbout: vi.fn(), onPrivacy: vi.fn(), onClose: vi.fn(),
    };
    const view = inFrame(<SettingsSheet theme="auto" unit="miles" version="1.4.0" errorCount={0} access="everything" removed={removed(37)} moves={1} {...calls} {...more} />, frame(platform));
    return { calls, view };
  }

  it('chooses the theme, Auto among them', async () => {
    const { calls } = show();
    const themes = screen.getByRole('radiogroup', { name: 'Theme' });
    expect(within(themes).getByRole('radio', { name: 'Auto' })).toHaveAttribute('aria-checked', 'true');
    await userEvent.click(within(themes).getByRole('radio', { name: 'Light' }));
    expect(calls.onTheme).toHaveBeenCalledWith('light');
    expect(screen.getByText('PhotoSense 1.4.0 · Runs on this phone. Nothing is uploaded.')).toBeInTheDocument();
  });

  it('chooses miles or kilometers for distances', async () => {
    const { calls } = show();
    const units = screen.getByRole('radiogroup', { name: 'Distances' });
    expect(within(units).getByRole('radio', { name: 'Miles' })).toHaveAttribute('aria-checked', 'true');
    await userEvent.click(within(units).getByRole('radio', { name: 'Kilometers' }));
    expect(calls.onUnit).toHaveBeenCalledWith('km');
  });

  it('says what PhotoSense may see, what waits in PhotoSense Removed, and how much Undo can put back', () => {
    const { view } = show();
    expect(screen.getByRole('button', { name: 'Photo access Full access' })).toBeInTheDocument();
    expect(screen.getByText('PhotoSense Removed').closest('li')).toHaveTextContent('PhotoSense Removed 37 items · 212.0 MB');
    expect(screen.getByRole('button', { name: 'Delete permanently…' })).toBeEnabled();
    expect(screen.getByText('Delete permanently…')).not.toHaveClass('text-t3');
    expect(screen.getByRole('button', { name: 'Recently moved 1' })).toBeInTheDocument();
    view.again(<SettingsSheet theme="dark" unit="km" errorCount={0} access="selected" removed={removed(1, 2048)} {...noCalls()} />, frame('android'));
    expect(screen.getByRole('button', { name: 'Photo access Some chosen' })).toBeInTheDocument();
    expect(screen.getByText('PhotoSense Removed').closest('li')).toHaveTextContent('PhotoSense Removed 1 item · 2 KB');
    view.again(<SettingsSheet theme="dark" unit="km" errorCount={0} access="everything" {...noCalls()} />, frame('android'));
    expect(screen.getByRole('button', { name: 'Photo access All photos and videos' })).toBeInTheDocument();
  });

  it('puts itself away before what a row leads to opens', async () => {
    const { calls } = show();
    const rows = [
      ['Photo access Full access', calls.onPhotoAccess], ['Scan again', calls.onScanAgain], ['Delete permanently…', calls.onDeletePermanently], ['Recently moved 1', calls.onRecent],
      ['Errors None', calls.onErrors], ['Help', calls.onHelp], ['About PhotoSense 1.4.0', calls.onAbout], ['Privacy', calls.onPrivacy],
    ] as const;
    for (const [row, call] of rows) {
      await userEvent.click(screen.getByRole('button', { name: row }));
      expect(call).toHaveBeenCalledOnce();
    }
    expect(calls.onClose).toHaveBeenCalledTimes(8);
  });

  it('counts the errors, says where it runs before the version is known, and offers no deleting while nothing waits or before it is known', () => {
    const { view } = show('ios', { errorCount: 2, version: undefined });
    expect(screen.getByRole('button', { name: 'Errors 2' })).toBeInTheDocument();
    expect(screen.getByText('Runs on this phone. Nothing is uploaded.')).toBeInTheDocument();
    view.again(<SettingsSheet theme="dark" unit="km" errorCount={0} access="nothing" removed={removed(0)} {...noCalls()} />, frame('ios', { tablet: true }));
    expect(screen.getByText('Runs on this iPad. Nothing is uploaded.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Photo access None' })).toBeInTheDocument();
    expect(screen.getByText('Empty')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete permanently…' })).toBeDisabled();
    expect(screen.getByText('Delete permanently…')).toHaveClass('text-t3');
    view.again(<SettingsSheet theme="dark" unit="km" errorCount={0} {...noCalls()} />, frame('android', { tablet: true }));
    expect(screen.getByText('Runs on this tablet. Nothing is uploaded.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Photo access' })).toBeInTheDocument();
    expect(screen.getByText('…')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete permanently…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Recently moved' })).toBeInTheDocument();
  });
});

/** Every action of Settings, done by nothing. */
function noCalls() {
  return {
    onTheme: vi.fn(), onUnit: vi.fn(), onPhotoAccess: vi.fn(), onScanAgain: vi.fn(), onDeletePermanently: vi.fn(), onRecent: vi.fn(), onErrors: vi.fn(), onHelp: vi.fn(),
    onAbout: vi.fn(), onPrivacy: vi.fn(), onClose: vi.fn(),
  };
}

describe('Delete permanently', () => {
  const waiting = { files: 37, bytes: 212 * 1024 ** 2, folders: [{ path: '/Photo Library/_PhotoSense_Removed', files: 37, bytes: 212 * 1024 ** 2 }] };
  const question = () => screen.getByRole('dialog');

  it('says where everything goes, then asks once more, where it can be told not to ask again', async () => {
    const onErase = vi.fn(), onCancel = vi.fn();
    inFrame(<DeletePermanently removed={waiting} askTwice busy={false} onErase={onErase} onCancel={onCancel} />);
    expect(question()).toHaveAccessibleName('Delete 37 items permanently?');
    expect(question()).toHaveTextContent('Everything in PhotoSense Removed (212.0 MB) goes to Recently Deleted, where iOS keeps it for 30 days.');
    expect(question()).toHaveTextContent('Undo can no longer bring these back, and they leave Recently moved.');
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 37 items' }));
    expect(onErase).not.toHaveBeenCalled();
    expect(question()).toHaveAccessibleName('Are you sure?');
    expect(question()).toHaveTextContent('The 37 items in PhotoSense Removed go to Recently Deleted. Undo can no longer bring them back.');
    await userEvent.click(within(question()).getByRole('switch', { name: 'Do not ask me this second time again' }));
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 37 items' }));
    expect(onErase).toHaveBeenCalledExactlyOnceWith(true);
    await userEvent.click(within(question()).getByRole('button', { name: 'Cancel' }));
    expect(onCancel).toHaveBeenCalledOnce();
  });

  it('asks only once when told not to ask twice, in Android words, and names one as one', async () => {
    const onErase = vi.fn();
    const { again } = inFrame(<DeletePermanently removed={waiting} askTwice={false} busy={false} onErase={onErase} onCancel={vi.fn()} />, frame('android'));
    expect(question()).toHaveTextContent('Everything in Pictures/PhotoSense Removed (212.0 MB) goes to the bin, where Android keeps it for 30 days.');
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 37 items' }));
    expect(onErase).toHaveBeenCalledExactlyOnceWith(false);
    const one = { files: 1, bytes: 1024, folders: [] };
    again(<DeletePermanently removed={one} askTwice busy onErase={onErase} onCancel={vi.fn()} />);
    expect(question()).toHaveAccessibleName('Delete 1 item permanently?');
    expect(within(question()).getByRole('button', { name: 'Delete 1 item' })).toBeDisabled();
  });

  it('says the one item goes, and that Undo cannot bring it back', async () => {
    inFrame(<DeletePermanently removed={{ files: 1, bytes: 1024, folders: [] }} askTwice busy={false} onErase={vi.fn()} onCancel={vi.fn()} />);
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 1 item' }));
    expect(question()).toHaveTextContent('The 1 item in PhotoSense Removed goes to Recently Deleted. Undo can no longer bring it back.');
  });
});

describe('PhotoSense on a phone', () => {
  const notify = vi.fn(), onSettings = vi.fn(), onAbout = vi.fn();
  let settings: AppSettings = { eraseAskTwice: true, distanceUnit: 'miles' };
  beforeEach(() => {
    localStorage.setItem('photosense-first-run', 'done');
    settings = { eraseAskTwice: true, distanceUnit: 'miles' };
    for (const f of [notify, onSettings, onAbout]) f.mockReset();
  });

  function App({ platform = 'ios', start = 'cleanup' as TabId }: { platform?: Platform; start?: TabId }) {
    const [tab, setTab] = React.useState<TabId>(start);
    const [theme, setTheme] = React.useState<'auto' | 'dark' | 'light'>('auto');
    return (
      <PhoneApp platform={platform} tab={tab} status={{ instanceId: 's', completed: '2026-11-10T16:25:00Z', totalPhotos: 2 }}
        toasts={[]} theme={theme} version="1.4.0" errorCount={0} settings={settings} onSettings={onSettings} onTab={setTab} removeToast={vi.fn()} onTheme={setTheme}
        scanning={false} onStarted={vi.fn()} onStopScan={vi.fn()} notify={notify} onErrors={vi.fn()} onHelp={vi.fn()} onAbout={onAbout} />
    );
  }

  it('gives each tab its header, in the platform\'s words, and the gear opens Settings', async () => {
    width(false);
    render(<App platform="android" />);
    expect(screen.getByRole('heading', { name: 'Clean up' })).toBeInTheDocument();
    expect(screen.getByText('Copies of the same photo or video')).toBeInTheDocument();
    const tabs = screen.getByRole('navigation', { name: 'Tabs' });
    await userEvent.click(within(tabs).getByRole('button', { name: 'Organize' }));
    expect(screen.getByText('Sort Phone into folders by place and date')).toBeInTheDocument();
    await userEvent.click(within(tabs).getByRole('button', { name: 'Send' }));
    expect(screen.getByText('Copy or move to PhotoSense on a Windows PC or Mac')).toBeInTheDocument();
    await userEvent.click(within(tabs).getByRole('button', { name: 'Library' }));
    expect(screen.getByRole('heading', { name: 'Library' })).toBeInTheDocument();

    await userEvent.click(screen.getAllByRole('button', { name: 'Settings' })[0]);
    const settings = screen.getByRole('dialog', { name: 'Settings' });
    await userEvent.click(within(settings).getByRole('radio', { name: 'Dark' }));
    expect(within(settings).getByRole('radio', { name: 'Dark' })).toHaveAttribute('aria-checked', 'true');
    await userEvent.click(within(settings).getByRole('button', { name: 'Done' }));
    expect(screen.queryByRole('dialog', { name: 'Settings' })).not.toBeInTheDocument();
  });

  it('sends what is chosen in Library: Send opens Library choosing, and Send in Library comes back with the choice', async () => {
    width(false);
    render(<App />);
    const tabs = () => screen.getByRole('navigation', { name: 'Tabs' });
    await userEvent.click(within(tabs()).getByRole('button', { name: 'Send' }));
    await userEvent.click(screen.getByRole('radio', { name: /^What you chose in LibraryNothing chosen yet/ }));
    // Library, choosing: its selection bar in place of the tabs.
    expect(screen.getByRole('toolbar')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'IMG_1.JPG' }));
    await userEvent.click(within(screen.getByRole('toolbar')).getByRole('button', { name: 'Send' }));
    expect(screen.getByRole('radio', { name: 'What you chose in Library1 item' })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('button', { name: 'Offer 1 item to a computer' })).toBeEnabled();
  });

  describe('from Settings', () => {
    const MB = 1024 ** 2;
    const waiting = { files: 37, bytes: 212 * MB, folders: [{ path: '/Photo Library/_PhotoSense_Removed', files: 30, bytes: 200 * MB }, { path: '/Photo Library/Trips/_PhotoSense_Removed', files: 7, bytes: 12 * MB }] };
    const gear = async () => { await userEvent.click(screen.getAllByRole('button', { name: 'Settings' })[0]); return screen.getByRole('dialog', { name: 'Settings' }); };
    const question = () => screen.getByRole('dialog');
    beforeEach(() => {
      width(false);
      api.useRemoved.mockImplementation((roots: string[] | null) => ({ data: roots ? waiting : undefined }));
      api.eraseRemoved.mockResolvedValue({ erased: 37, bytes: 212 * MB, skipped: 0, problems: [] });
    });

    it('deletes permanently after both questions, remembering not to ask twice again when told', async () => {
      render(<App />);
      await userEvent.click(within(await gear()).getByRole('button', { name: 'Delete permanently…' }));
      expect(api.useRemoved).toHaveBeenLastCalledWith(['/Photo Library']);
      await userEvent.click(within(question()).getByRole('button', { name: 'Delete 37 items' }));
      await userEvent.click(within(question()).getByRole('switch', { name: 'Do not ask me this second time again' }));
      await userEvent.click(within(question()).getByRole('button', { name: 'Delete 37 items' }));
      expect(api.eraseRemoved).toHaveBeenCalledExactlyOnceWith(['/Photo Library/_PhotoSense_Removed', '/Photo Library/Trips/_PhotoSense_Removed']);
      expect(onSettings).toHaveBeenCalledExactlyOnceWith({ eraseAskTwice: false });
      expect(notify).toHaveBeenCalledExactlyOnceWith('37 items deleted. They wait in Recently Deleted for 30 days.', 'ok');
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      // Nothing is asked of the service while no sheet needs it.
      expect(api.useRemoved).toHaveBeenLastCalledWith(null);
    });

    it('asks once when told not to ask twice, and says what could not be deleted, that nothing was left, or what went wrong', async () => {
      settings = { eraseAskTwice: false, distanceUnit: 'miles' };
      api.eraseRemoved.mockClear();
      api.eraseRemoved.mockResolvedValueOnce({ erased: 35, bytes: 1, skipped: 2, problems: ['You chose not to delete them.'] });
      render(<App platform="android" />);
      const once = async () => {
        await userEvent.click(within(await gear()).getByRole('button', { name: 'Delete permanently…' }));
        await userEvent.click(within(question()).getByRole('button', { name: 'Delete 37 items' }));
      };
      await once();
      expect(notify.mock.calls).toEqual([['35 items deleted. They wait in the bin for 30 days.', 'ok'], ['2 items could not be deleted. You chose not to delete them.', 'error']]);
      api.eraseRemoved.mockResolvedValueOnce({ erased: 0, bytes: 0, skipped: 1, problems: [] });
      await once();
      expect(notify).toHaveBeenLastCalledWith('1 item could not be deleted.', 'error');
      api.eraseRemoved.mockResolvedValueOnce({ erased: 0, bytes: 0, skipped: 0, problems: [] });
      await once();
      expect(notify).toHaveBeenLastCalledWith('There was nothing left to delete.', 'info');
      api.eraseRemoved.mockRejectedValueOnce(new Error('Photo access was taken away.'));
      await once();
      expect(notify).toHaveBeenLastCalledWith('Photo access was taken away.', 'error');
      api.eraseRemoved.mockRejectedValueOnce('no');
      await once();
      expect(notify).toHaveBeenLastCalledWith('no', 'error');
      expect(onSettings).not.toHaveBeenCalled();
      // Cancel asks nothing.
      await userEvent.click(within(await gear()).getByRole('button', { name: 'Delete permanently…' }));
      await userEvent.click(within(question()).getByRole('button', { name: 'Cancel' }));
      expect(api.eraseRemoved).toHaveBeenCalledTimes(5);
    });

    it('opens the phone\'s own settings for photo access, saying when it could not, and Recently moved with Undo, and About at Privacy', async () => {
      api.useOrganizeBatches.mockReturnValue({ data: [{ id: 'b1', label: '/Photo Library/Butte', mode: 'move', count: 2, bytes: 10, utc: new Date().toISOString() }] });
      api.undoChange.mockResolvedValue({ restored: 2, skipped: 0, problems: [] });
      api.openPhotoSettings.mockResolvedValueOnce(undefined).mockRejectedValueOnce(new Error('No settings here.'));
      render(<App />);
      await userEvent.click(within(await gear()).getByRole('button', { name: /^Photo access/ }));
      expect(api.openPhotoSettings).toHaveBeenCalledOnce();
      await userEvent.click(within(await gear()).getByRole('button', { name: /^Photo access/ }));
      await act(async () => {});
      expect(notify).toHaveBeenLastCalledWith('No settings here.', 'error');

      await userEvent.click(within(await gear()).getByRole('button', { name: 'Recently moved 1' }));
      const recent = screen.getByRole('dialog', { name: 'Recently moved' });
      await userEvent.click(within(recent).getByRole('button', { name: 'Undo' }));
      expect(api.undoChange).toHaveBeenCalledExactlyOnceWith('b1');
      expect(notify).toHaveBeenLastCalledWith('Put back. Every file is where it was.', 'ok');
      await userEvent.click(within(recent).getByRole('button', { name: 'Done' }));
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      api.useOrganizeBatches.mockReturnValue({});
      await userEvent.click(within(await gear()).getByRole('button', { name: 'Recently moved' }));
      expect(screen.getByRole('dialog', { name: 'Recently moved' })).toHaveTextContent('Nothing to undo.');
      await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Done' }));

      await userEvent.click(within(await gear()).getByRole('button', { name: 'Privacy' }));
      await userEvent.click(within(await gear()).getByRole('button', { name: 'About PhotoSense 1.4.0' }));
      expect(onAbout.mock.calls).toEqual([['privacy'], []]);
      await userEvent.click(within(await gear()).getByRole('radio', { name: 'Kilometers' }));
      expect(onSettings).toHaveBeenLastCalledWith({ distanceUnit: 'km' });
    });
  });

  it('pushes a review over the list on a phone, without the tabs, and Back takes it back', async () => {
    width(false);
    render(<App />);
    await userEvent.click(within(screen.getByRole('list', { name: 'Groups' })).getByRole('button', { name: /IMG_4299\.JPG/ }));
    expect(screen.getByRole('region', { name: 'Review' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Clean up' })).not.toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Tabs' })).not.toBeInTheDocument();
    await act(async () => { history.back(); await new Promise(r => setTimeout(r, 20)); });
    expect(screen.getByRole('heading', { name: 'Clean up' })).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Tabs' })).toBeInTheDocument();
  });

  it('scans the whole library again from under the list of Clean up', async () => {
    width(false);
    api.libraryRoot.mockResolvedValue('/Photo Library');
    api.startScan.mockResolvedValue({ instanceId: 'scan-2' });
    render(<App />);
    await userEvent.click(screen.getByRole('button', { name: 'Scan again' }));
    expect(api.startScan).toHaveBeenCalledWith({ primaryLocation: '/Photo Library', recursive: true });
  });

  it('goes back to the list when the Clean up tab is tapped again, and leaves another tab as it is', async () => {
    width(false);
    render(<App />);
    const tabs = () => screen.getByRole('navigation', { name: 'Tabs' });
    await userEvent.click(within(tabs()).getByRole('button', { name: 'Clean up' }));
    expect(screen.getByRole('heading', { name: 'Clean up' })).toBeInTheDocument();
    await userEvent.click(within(tabs()).getByRole('button', { name: 'Library' }));
    await userEvent.click(within(tabs()).getByRole('button', { name: 'Library' }));
    expect(screen.getByRole('heading', { name: 'Library' })).toBeInTheDocument();
  });

  it('pushes a suggestion\'s preview over Organize\'s list on a phone, which Back or the tab takes back', async () => {
    width(false);
    render(<App start="organize" />);
    const tabs = () => screen.getByRole('navigation', { name: 'Tabs' });
    const open = () => userEvent.click(within(screen.getByRole('list', { name: 'Suggestions' })).getByRole('button', { name: /^Anaconda/ }));
    await open();
    expect(screen.getByRole('region', { name: 'Preview' })).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Tabs' })).not.toBeInTheDocument();
    await act(async () => { history.back(); await new Promise(r => setTimeout(r, 20)); });
    expect(screen.getByRole('heading', { level: 1, name: 'Organize' })).toBeInTheDocument();
    await userEvent.click(within(tabs()).getByRole('button', { name: 'Organize' }));
    expect(screen.getByRole('heading', { level: 1, name: 'Organize' })).toBeInTheDocument();
  });

  it('from Organize shows Library\'s files with no location, and offers there the albums named in Organize', async () => {
    width(false);
    render(<App start="organize" />);
    await userEvent.click(screen.getByRole('button', { name: 'New album' }));
    await userEvent.type(screen.getByRole('textbox', { name: 'Album name' }), 'Lake{Enter}');
    await userEvent.click(screen.getByRole('button', { name: '1 file has no location. It stays where it is.' }));
    expect(screen.getByRole('heading', { level: 1, name: 'Library' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'No location 1' })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(screen.getByRole('button', { name: 'Select' }));
    await userEvent.click(screen.getByRole('button', { name: 'IMG_2.JPG' }));
    await userEvent.click(within(screen.getByRole('toolbar')).getByRole('button', { name: 'Add to album' }));
    expect(within(screen.getByRole('dialog', { name: 'Add 1 to an album' })).getByRole('button', { name: 'Lake' })).toBeInTheDocument();
  });

  it('counts the library beside its tab on an iPad, and while choosing in Library puts the selection bar in place of the tabs', async () => {
    width(true);
    const view = render(<App start="library" />);
    expect(within(screen.getByRole('navigation', { name: 'Tabs' })).getByRole('button', { name: 'Library 2' })).toBeInTheDocument();
    expect(within(screen.getByRole('navigation', { name: 'Tabs' })).getByRole('button', { name: 'Organize 1' })).toBeInTheDocument();
    expect(within(screen.getByRole('navigation', { name: 'Tabs' })).getByRole('button', { name: 'Clean up 1' })).toBeInTheDocument();
    view.unmount();
    width(false);
    render(<App start="library" />);
    await userEvent.click(screen.getByRole('button', { name: 'Select' }));
    expect(screen.queryByRole('navigation', { name: 'Tabs' })).not.toBeInTheDocument();
    expect(screen.getByRole('toolbar', { name: 'What is selected' })).toBeInTheDocument();
  });

  it('shows a review beside the list on a tablet, so nothing is pushed', () => {
    width(true);
    render(<App start="cleanup" />);
    expect(screen.getByRole('heading', { name: 'Clean up' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'IMG_4299.JPG' })).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Tabs' })).toBeInTheDocument();
  });
});
