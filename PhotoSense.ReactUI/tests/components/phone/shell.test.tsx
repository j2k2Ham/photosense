import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PhoneApp } from '../../../components/phone/PhoneApp';
import { PhoneShell } from '../../../components/phone/PhoneShell';
import { PushedHeader, PushedScreen, RootHeader, Screen, SplitView } from '../../../components/phone/Screens';
import { SettingsSheet } from '../../../components/phone/SettingsSheet';
import type { Platform, TabId } from '../../../lib/phone';
import { frame, inFrame, width } from './frame';
import { group, groupsPage, photo } from './groups';

// The phone has said PhotoSense may see everything; what comes before that is tested with the first run. Clean up,
// tested on its own, has one group of duplicates.
const api = vi.hoisted(() => ({ usePhotoAccess: vi.fn(), useGroupsThrough: vi.fn(), libraryRoot: vi.fn(), startScan: vi.fn(), useOrganizeBatches: vi.fn(() => ({})), useSendState: vi.fn(() => ({})) }));
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
  function show(platform: Platform = 'ios', more = {}) {
    const calls = { onTheme: vi.fn(), onUnit: vi.fn(), onScanAgain: vi.fn(), onDeletePermanently: vi.fn(), onErrors: vi.fn(), onHelp: vi.fn(), onAbout: vi.fn(), onClose: vi.fn() };
    const view = inFrame(<SettingsSheet theme="auto" unit="miles" version="1.4.0" errorCount={0} {...calls} {...more} />, frame(platform));
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

  it('puts itself away before what a row leads to opens', async () => {
    const { calls } = show();
    for (const [row, call] of [['Scan again', calls.onScanAgain], ['Delete permanently…', calls.onDeletePermanently], ['Errors None', calls.onErrors], ['Help', calls.onHelp], ['About PhotoSense 1.4.0', calls.onAbout]] as const) {
      await userEvent.click(screen.getByRole('button', { name: row }));
      expect(call).toHaveBeenCalledOnce();
    }
    expect(calls.onClose).toHaveBeenCalledTimes(5);
  });

  it('counts the errors, and says where it runs before the version is known', () => {
    const { view } = show('ios', { errorCount: 2, version: undefined });
    expect(screen.getByRole('button', { name: 'Errors 2' })).toBeInTheDocument();
    expect(screen.getByText('Runs on this phone. Nothing is uploaded.')).toBeInTheDocument();
    view.again(<SettingsSheet theme="dark" unit="km" errorCount={0} onTheme={vi.fn()} onUnit={vi.fn()} onScanAgain={vi.fn()} onDeletePermanently={vi.fn()} onErrors={vi.fn()} onHelp={vi.fn()} onAbout={vi.fn()} onClose={vi.fn()} />, frame('ios', { tablet: true }));
    expect(screen.getByText('Runs on this iPad. Nothing is uploaded.')).toBeInTheDocument();
    view.again(<SettingsSheet theme="dark" unit="km" errorCount={0} onTheme={vi.fn()} onUnit={vi.fn()} onScanAgain={vi.fn()} onDeletePermanently={vi.fn()} onErrors={vi.fn()} onHelp={vi.fn()} onAbout={vi.fn()} onClose={vi.fn()} />, frame('android', { tablet: true }));
    expect(screen.getByText('Runs on this tablet. Nothing is uploaded.')).toBeInTheDocument();
  });
});

describe('PhotoSense on a phone', () => {
  beforeEach(() => localStorage.setItem('photosense-first-run', 'done'));

  function App({ platform = 'ios', start = 'cleanup' as TabId }: { platform?: Platform; start?: TabId }) {
    const [tab, setTab] = React.useState<TabId>(start);
    const [theme, setTheme] = React.useState<'auto' | 'dark' | 'light'>('auto');
    return (
      <PhoneApp platform={platform} tab={tab} status={{ instanceId: 's', completed: '2026-11-10T16:25:00Z', totalPhotos: 2 }}
        toasts={[]} theme={theme} version="1.4.0" errorCount={0} unit="miles" onUnit={vi.fn()} onTab={setTab} removeToast={vi.fn()} onTheme={setTheme}
        scanning={false} onStarted={vi.fn()} onStopScan={vi.fn()} notify={vi.fn()} onDeletePermanently={vi.fn()} onErrors={vi.fn()} onHelp={vi.fn()} onAbout={vi.fn()} />
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
