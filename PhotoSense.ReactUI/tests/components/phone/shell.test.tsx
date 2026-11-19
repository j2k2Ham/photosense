import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PhoneApp } from '../../../components/phone/PhoneApp';
import { PhoneShell } from '../../../components/phone/PhoneShell';
import { LayerScreen, PlaceContext, PushedHeader, PushedScreen, RootHeader, Screen, SplitView } from '../../../components/phone/Screens';
import { DeletePermanently, SettingsSheet } from '../../../components/phone/SettingsSheet';
import type { Place, Platform, TabId } from '../../../lib/phone';
import type { AppSettings } from '../../../lib/settings';
import { frame, inFrame, width } from './frame';
import { group, groupsPage, photo } from './groups';
import { backupState, location, locations, plan, safeReview, swipeCard, swipeState } from '../../fixtures';
import type { PlanDto, SwipeStateDto } from '../../../types';

// The phone has said PhotoSense may see everything; what comes before that is tested with the first run. Clean up,
// tested on its own, has one group of duplicates.
const api = vi.hoisted(() => ({ openedNotice: vi.fn(async (): Promise<{ opens: string | null }> => ({ opens: null })), useNotices: vi.fn((): { data?: unknown } => ({})), changeNotices: vi.fn(), useCompression: vi.fn((): { data?: unknown } => ({})), useYear: vi.fn((): { data?: unknown } => ({})), usePhotoAccess: vi.fn(), useGroupsThrough: vi.fn(), libraryRoot: vi.fn(), startScan: vi.fn(), useOrganizeBatches: vi.fn(() => ({})),
  useBackup: vi.fn(() => ({})), useLibraryBackup: vi.fn(() => ({})), useSafeReview: vi.fn(() => ({})), backUpIfDue: vi.fn(async () => ({})), startBackup: vi.fn(async () => ({})), removeSafe: vi.fn(), stopPairing: vi.fn(async () => undefined), usePairing: vi.fn(() => ({})),
  useRemoved: vi.fn<(roots: string[] | null) => { data?: unknown }>(() => ({})), eraseRemoved: vi.fn(), openPhotoSettings: vi.fn(), undoChange: vi.fn(),
  usePlan: vi.fn<() => { data?: PlanDto }>(() => ({})),
  useSwipe: vi.fn<() => { data?: SwipeStateDto }>(() => ({})), decideSwipe: vi.fn(),
  useCategories: vi.fn(() => ({ data: { categories: [{ category: 'duplicates', count: 1, bytes: 0 }, { category: 'screenshots', count: 0, bytes: 0 }], unmeasured: 0, storageTotal: null, storageFree: null } })),
  useCategoryPhotos: vi.fn(() => ({ data: [] })),
  useTrips: vi.fn((): { data?: unknown } => ({})), useFixes: vi.fn((): { data?: unknown } => ({})),
  useLocations: vi.fn((): { data?: unknown } => ({})), locationsIfDue: vi.fn(async () => undefined), sendToLocation: vi.fn(async () => undefined),
  useLocationDetails: vi.fn(() => ({})),
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

  it('of a tablet\'s detail: no back, the title on the left, and the gear only with an iPad\'s sidebar hidden, Settings being at its foot', () => {
    const { again } = inFrame(<PushedHeader title="IMG_4299.JPG" gear />, frame('ios', { tablet: true }));
    expect(screen.queryByRole('button', { name: 'Back' })).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'IMG_4299.JPG' })).toHaveClass('text-[22px]', 'font-bold');
    expect(screen.queryByRole('button', { name: 'Settings' })).not.toBeInTheDocument();
    again(<PushedHeader title="IMG_4299.JPG" onBack={vi.fn()} gear />, frame('ios', { tablet: true, sidebarHidden: true }));
    expect(screen.getAllByRole('button')).toHaveLength(2);
    again(<PushedHeader title="Anaconda" gear />, frame('android', { tablet: true }));
    expect(screen.queryByRole('button', { name: 'Settings' })).not.toBeInTheDocument();
  });

  it('of a tablet\'s first screen: what stands beside the title, such as the search field', () => {
    const { again } = inFrame(<RootHeader title="Library" subtitle="16 photos and videos" side={<p>the search field</p>} />, frame('ios', { tablet: true }));
    expect(screen.getByText('the search field').parentElement).toHaveClass('flex', 'items-center');
    expect(screen.getByText('16 photos and videos')).toBeInTheDocument();
    again(<RootHeader title="Library" side={<p>the search field</p>} />, frame('android', { tablet: true }));
    expect(screen.getByText('the search field').parentElement).toHaveClass('pr-3');
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
    // The growth screens' lists are 380 pixels on both.
    again(<SplitView narrow list={<p>the list</p>} detail={<p>the detail</p>} />, frame('android', { tablet: true }));
    expect(screen.getByText('the list').parentElement).toHaveClass('w-[380px]');
  });

  it('in place of a tab on a tablet, is drawn there with its foot, and neither Back nor Escape takes it away', async () => {
    const back = vi.fn();
    inFrame(<PlaceContext.Provider value><LayerScreen label="Trips" onBack={back} footer={<p>the foot</p>}>x</LayerScreen></PlaceContext.Provider>, frame('ios', { tablet: true }));
    const shown = screen.getByRole('region', { name: 'Trips' });
    expect(shown).not.toHaveClass('fixed');
    expect(screen.getByText('the foot').parentElement).toHaveClass('border-t');
    await userEvent.keyboard('{Escape}');
    expect(back).not.toHaveBeenCalled();
  });
});

describe('the shell', () => {
  const screens = { library: <p>library screen</p>, cleanup: <p>cleanup screen</p>, organize: <p>organize screen</p>, backup: <p>backup screen</p> };
  function Shell({ platform = 'ios', start = 'library', foot = 'tabs' as const }: { platform?: Platform; start?: TabId; foot?: 'tabs' | 'selection' | 'none' }) {
    const [tab, setTab] = React.useState<TabId>(start);
    const [place, setPlace] = React.useState<Place>();
    return <PhoneShell platform={platform} tab={tab} foot={foot} counts={{ cleanup: '264', trips: '6' }} screens={screens} toasts={[]} onTab={t => { setPlace(undefined); setTab(t); }} onSettings={vi.fn()} removeToast={vi.fn()}
      place={place} onPlace={setPlace} placeScreen={<p>the settings</p>} notice={<p>the notice</p>} sheet={<p>the sheet</p>} />;
  }

  it('on an iPhone: the tabs in a floating glass bar, each tab kept as it was once turned to', async () => {
    width(false);
    render(<Shell />);
    const tabs = screen.getByRole('navigation', { name: 'Tabs' });
    expect(tabs).toHaveClass('bottom-[22px]', 'rounded-full');
    expect(within(tabs).getAllByRole('button').map(b => b.textContent)).toEqual(['Library', 'Clean up', 'Organize', 'Backup']);
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
    expect(within(side).getByRole('heading', { name: 'Collections' })).toBeInTheDocument();
    // A collection or Settings shows in place of the tab, lit in the sidebar; the tab showing is not.
    await userEvent.click(within(side).getByRole('button', { name: 'Trips 6' }));
    expect(within(side).getByRole('button', { name: 'Trips 6' })).toHaveAttribute('aria-current', 'page');
    expect(within(side).getByRole('button', { name: 'Clean up 264' })).not.toHaveAttribute('aria-current');
    expect(screen.getByText('cleanup screen').parentElement).toHaveClass('hidden');
    // Library draws the collections, so it is drawn out of sight.
    expect(screen.getByText('library screen').parentElement).toHaveClass('hidden');
    await userEvent.click(within(side).getByRole('button', { name: 'Settings' }));
    expect(screen.getByText('the settings')).toBeInTheDocument();
    await userEvent.click(within(side).getByRole('button', { name: 'Needs fixing' }));
    expect(screen.queryByText('the settings')).not.toBeInTheDocument();
    // Hidden, the sidebar leaves no tab lit while a collection shows.
    await userEvent.click(within(side).getByRole('button', { name: 'Hide sidebar' }));
    expect(within(screen.getByRole('navigation', { name: 'Tabs' })).getAllByRole('button').filter(b => b.getAttribute('aria-current'))).toHaveLength(0);
    await userEvent.click(screen.getByRole('button', { name: 'Show sidebar' }));
    await userEvent.click(within(screen.getByRole('navigation', { name: 'Tabs' })).getByRole('button', { name: 'Library' }));
    expect(within(screen.getByRole('navigation', { name: 'Tabs' })).getByRole('button', { name: 'Library' })).toHaveAttribute('aria-current', 'page');
    await userEvent.click(screen.getByRole('button', { name: 'Hide sidebar' }));
    const floating = screen.getByRole('navigation', { name: 'Tabs' });
    expect(floating).toHaveClass('-translate-x-1/2');
    await userEvent.click(within(floating).getByRole('button', { name: 'Backup' }));
    expect(within(floating).getByRole('button', { name: 'Backup' })).toHaveAttribute('aria-current', 'page');
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
    // Library's collections under a line, and Settings at the foot.
    await userEvent.click(within(rail).getByRole('button', { name: 'Hidden' }));
    expect(within(rail).getByRole('button', { name: 'Hidden' })).toHaveAttribute('aria-current', 'page');
    await userEvent.click(within(rail).getByRole('button', { name: 'Settings' }));
    expect(within(rail).getByRole('button', { name: 'Settings' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByText('the settings')).toBeInTheDocument();
    await userEvent.click(within(rail).getByRole('button', { name: 'Organize' }));
    expect(screen.queryByText('the settings')).not.toBeInTheDocument();
    expect(container.querySelectorAll('[aria-hidden="true"].fixed')[1]).toHaveClass('bg-bg');
  });
});

describe('Settings on a tablet (T5)', () => {
  it('stands beside the sidebar with Subscription beside it, the plan lit, and stays while a row opens what it leads to', async () => {
    const onClose = vi.fn(), onRecent = vi.fn();
    const none = () => undefined;
    inFrame(
      <PlaceContext.Provider value>
        <SettingsSheet theme="auto" unit="miles" errorCount={0} plan={plan()} beside={<p>the subscription</p>} onClose={onClose} onRecent={onRecent}
          onTheme={none} onUnit={none} onPhotoAccess={none} onScanAgain={none} onDeletePermanently={none} onErrors={none} onHelp={none} onAbout={none} onPrivacy={none}
          onSubscription={none} onPair={none} onLowStorage={none} />
      </PlaceContext.Provider>, frame('ios', { tablet: true }));
    expect(screen.getByRole('region', { name: 'Settings' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Settings' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Back' })).not.toBeInTheDocument();
    expect(screen.getByText('the subscription')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /\. Subscription$/ })).toHaveAttribute('aria-current', 'true');
    await userEvent.click(screen.getByRole('button', { name: 'Recently moved' }));
    expect(onRecent).toHaveBeenCalledOnce();
    expect(onClose).not.toHaveBeenCalled();
  });
});

describe('Settings', () => {
  const removed = (files: number, bytes = 212 * 1024 ** 2) => ({ files, bytes, folders: files ? [{ path: '/Photo Library/_PhotoSense_Removed', files, bytes }] : [] });
  function show(platform: Platform = 'ios', more = {}) {
    const calls = {
      onTheme: vi.fn(), onUnit: vi.fn(), onPhotoAccess: vi.fn(), onScanAgain: vi.fn(), onDeletePermanently: vi.fn(), onRecent: vi.fn(), onErrors: vi.fn(), onHelp: vi.fn(),
      onAbout: vi.fn(), onPrivacy: vi.fn(), onClose: vi.fn(), onSubscription: vi.fn(), onPair: vi.fn(), onLowStorage: vi.fn(),
    };
    const view = inFrame(<SettingsSheet theme="auto" unit="miles" version="1.4.0" errorCount={0} access="everything" removed={removed(37)} moves={1} {...calls} {...more} />, frame(platform));
    return { calls, view };
  }

  it('turns low-storage alerts on or off under Clean up, once the phone has said whether they are', async () => {
    const { calls, view } = show();
    expect(screen.queryByRole('switch', { name: /^Low-storage alerts/ })).not.toBeInTheDocument();
    view.again(<SettingsSheet theme="auto" unit="miles" errorCount={0} lowStorage={false} {...calls} />);
    await userEvent.click(screen.getByRole('switch', { name: 'Low-storage alerts When less than 10% is free, at most once a week' }));
    expect(calls.onLowStorage).toHaveBeenCalledWith(true);
  });

  it('turns Remove location when sharing off or on under Privacy, on to start with', async () => {
    const onRemoveLocation = vi.fn();
    const { calls, view } = show();
    expect(screen.queryByRole('region', { name: 'Privacy' })).not.toBeInTheDocument();
    view.again(<SettingsSheet theme="auto" unit="miles" errorCount={0} onRemoveLocation={onRemoveLocation} {...calls} />);
    const location = screen.getByRole('switch', { name: 'Remove location when sharing Applies to copies you share from PhotoSense' });
    expect(location).toBeChecked();
    await userEvent.click(location);
    expect(onRemoveLocation).toHaveBeenCalledWith(false);
    view.again(<SettingsSheet theme="auto" unit="miles" errorCount={0} removeLocation={false} onRemoveLocation={onRemoveLocation} {...calls} />);
    expect(screen.getByRole('switch', { name: /^Remove location when sharing/ })).not.toBeChecked();
  });

  it('chooses the theme, Auto among them', async () => {
    const { calls } = show();
    const themes = screen.getByRole('radiogroup', { name: 'Theme' });
    expect(within(themes).getByRole('radio', { name: 'Auto' })).toHaveAttribute('aria-checked', 'true');
    await userEvent.click(within(themes).getByRole('radio', { name: 'Light' }));
    expect(calls.onTheme).toHaveBeenCalledWith('light');
    expect(screen.getByText('Your photos stay on your own devices. PhotoSense has no ads and does not track what is in your photos.')).toBeInTheDocument();
  });

  it('shows the plan first: the removals left with Get Pro on the free plan, the plan and its renewal on Pro; either opens Subscription', async () => {
    const { calls, view } = show('ios', { plan: plan() });
    const card = screen.getByRole('button', { name: 'PhotoSense Free. Subscription' });
    expect(card).toHaveTextContent('PhotoSense Free38 of 50 free removals left todayGet Pro');
    await userEvent.click(card);
    expect(calls.onSubscription).toHaveBeenCalledOnce();
    view.again(<SettingsSheet theme="dark" unit="km" errorCount={0} plan={plan({ tier: 'pro', kind: 'yearly', until: '2027-11-17T12:00:00Z' })} {...noCalls()} />);
    expect(screen.getByRole('button', { name: 'PhotoSense Pro. Subscription' })).toHaveTextContent('PhotoSense ProYearly plan · renews Nov 17, 2027');
    expect(screen.queryByText('Get Pro')).not.toBeInTheDocument();
  });

  it('names the computer backups go to, and where they go; with none, connects one; until the phone has said, has no Computer at all', async () => {
    const { calls, view } = show('ios', { computer: { name: 'Studio iMac', folder: 'Pictures/PhotoSense' } });
    const computer = screen.getByRole('region', { name: 'Computer' });
    expect(within(computer).getAllByRole('listitem').map(li => li.textContent)).toEqual(['Studio iMac Paired', 'Backup folder Pictures/PhotoSense']);
    view.again(<SettingsSheet theme="dark" unit="km" errorCount={0} computer={{ name: 'Studio iMac', folder: null }} {...noCalls()} />);
    expect(screen.getByText('Backup folder').closest('li')).toHaveTextContent(/^Backup folder$/);
    view.again(<SettingsSheet theme="dark" unit="km" errorCount={0} computer={null} {...calls} />);
    await userEvent.click(screen.getByRole('button', { name: 'Connect your computer' }));
    expect([calls.onClose.mock.calls.length, calls.onPair.mock.calls.length]).toEqual([1, 1]);
    view.again(<SettingsSheet theme="dark" unit="km" errorCount={0} {...noCalls()} />);
    expect(screen.queryByRole('region', { name: 'Computer' })).not.toBeInTheDocument();
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
      ['Errors None', calls.onErrors], ['Help', calls.onHelp], ['About PhotoSense 1.4.0', calls.onAbout], ['Privacy policy', calls.onPrivacy],
    ] as const;
    for (const [row, call] of rows) {
      await userEvent.click(screen.getByRole('button', { name: row }));
      expect(call).toHaveBeenCalledOnce();
    }
    expect(calls.onClose).toHaveBeenCalledTimes(8);
  });

  it('counts the errors, and offers no deleting while nothing waits or before it is known', () => {
    const { view } = show('ios', { errorCount: 2, version: undefined });
    expect(screen.getByRole('button', { name: 'Errors 2' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'About PhotoSense' })).toBeInTheDocument();
    view.again(<SettingsSheet theme="dark" unit="km" errorCount={0} access="nothing" removed={removed(0)} {...noCalls()} />, frame('ios', { tablet: true }));
    expect(screen.getByRole('button', { name: 'Photo access None' })).toBeInTheDocument();
    expect(screen.getByText('Empty')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete permanently…' })).toBeDisabled();
    expect(screen.getByText('Delete permanently…')).toHaveClass('text-t3');
    view.again(<SettingsSheet theme="dark" unit="km" errorCount={0} {...noCalls()} />, frame('android', { tablet: true }));
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
    onAbout: vi.fn(), onPrivacy: vi.fn(), onClose: vi.fn(), onSubscription: vi.fn(), onPair: vi.fn(), onLowStorage: vi.fn(),
  };
}

describe('Delete permanently', () => {
  const waiting = { files: 37, bytes: 212 * 1024 ** 2, folders: [{ path: '/Photo Library/_PhotoSense_Removed', files: 37, bytes: 212 * 1024 ** 2 }] };
  const question = () => screen.getByRole('dialog');

  it('says where everything goes, then asks once more, where it can be told not to ask again', async () => {
    const onErase = vi.fn(), onCancel = vi.fn();
    inFrame(<DeletePermanently removed={waiting} askTwice busy={false} onErase={onErase} onCancel={onCancel} />);
    expect(question()).toHaveAccessibleName('Delete 37 items permanently?');
    expect(question()).toHaveTextContent('This cannot be undone.Everything in PhotoSense Removed (212.0 MB) goes to Recently Deleted, where iOS keeps it for 30 days.');
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
  let settings: AppSettings = { eraseAskTwice: true, distanceUnit: 'miles', phonesSeen: false, removeLocationWhenSharing: true };
  beforeEach(() => {
    localStorage.setItem('photosense-first-run', 'done');
    settings = { eraseAskTwice: true, distanceUnit: 'miles', phonesSeen: false, removeLocationWhenSharing: true };
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

  /** Opens the duplicates' list from Find, on Clean up's first screen. */
  async function openDuplicates() {
    await userEvent.click(within(screen.getByRole('region', { name: 'Find' })).getByRole('button', { name: /^Duplicates/ }));
  }

  it('on a tablet shows Settings, with Subscription beside it, and a collection in place of the tab; Back, a tab or a notification moves on', async () => {
    width(true);
    api.usePlan.mockReturnValue({ data: plan() });
    const glacier = { id: 'gla', name: 'Glacier National Park', folder: '2026-07 Glacier National Park', start: '2026-07-18', end: '2026-07-21', photos: 12, videos: 0, latitude: 48.7, longitude: -113.8, cover: photo('c1'), folderMade: false, suggested: 'Glacier National Park' };
    api.useTrips.mockReturnValue({ data: { home: { town: 'Bozeman', region: 'Montana', country: 'US', latitude: 45.7, longitude: -111, days: 9 }, homeChosen: false, trips: [glacier], computer: null, tripsFolder: null, folder: null } });
    api.useFixes.mockReturnValue({ data: { total: 374 } });
    render(<App start="library" />);
    const side = screen.getByRole('navigation', { name: 'Tabs' });
    expect(within(side).getByRole('button', { name: 'Needs fixing 374' })).toBeInTheDocument();
    await userEvent.click(within(side).getByRole('button', { name: 'Settings' }));
    expect(screen.getByRole('region', { name: 'Settings' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Subscription' })).toBeInTheDocument();
    // The plan is beside it already.
    await userEvent.click(screen.getByRole('button', { name: /\. Subscription$/ }));
    expect(screen.queryByRole('dialog', { name: 'Subscription' })).not.toBeInTheDocument();
    act(() => history.back());
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Settings' })).not.toBeInTheDocument());
    await userEvent.click(within(side).getByRole('button', { name: 'Trips 1' }));
    expect(await screen.findByRole('region', { name: 'Glacier National Park' })).toBeInTheDocument();
    await userEvent.click(within(side).getByRole('button', { name: /^Organize/ }));
    expect(screen.queryByRole('region', { name: 'Glacier National Park' })).not.toBeInTheDocument();
    api.openedNotice.mockResolvedValueOnce({ opens: 'subscription' });
    await act(async () => { window.dispatchEvent(new Event('photosense-notice')); });
    expect(await screen.findByRole('region', { name: 'Settings' })).toBeInTheDocument();
    // With the sidebar hidden, the gear opens Settings in its place.
    await userEvent.click(within(screen.getByRole('navigation', { name: 'Tabs' })).getByRole('button', { name: /^Library/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Hide sidebar' }));
    await userEvent.click(screen.getAllByRole('button', { name: 'Settings' })[0]);
    expect(screen.getByRole('region', { name: 'Settings' })).toBeInTheDocument();
    for (const f of [api.usePlan, api.useTrips, api.useFixes]) f.mockReturnValue({});
  });

  it('opens Swipe to clean from Clean up over everything, and its summary when the set is through, or when it is closed with something marked', async () => {
    width(false);
    let today = swipeState({ set: [swipeCard('a'), swipeCard('b')] });
    api.useSwipe.mockImplementation(() => ({ data: today }));
    render(<App />);
    // Closed with nothing marked, Clean up is back.
    await userEvent.click(screen.getByRole('button', { name: 'Start' }));
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Swipe to clean' })).getByRole('button', { name: 'Close' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    // Back, with something marked: the summary, which closes to Clean up.
    today = swipeState({ set: [swipeCard('a', { delete: true }), swipeCard('b')] });
    await userEvent.click(screen.getByRole('button', { name: 'Start' }));
    await act(async () => { history.back(); await new Promise(r => setTimeout(r, 20)); });
    expect(screen.getByRole('dialog', { name: 'Swipe summary' })).toBeInTheDocument();
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Swipe summary' })).getByRole('button', { name: 'Close' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    // The last of the set swiped: the summary follows.
    api.decideSwipe.mockResolvedValue(swipeState({ set: [swipeCard('a', { delete: true }), swipeCard('b', { delete: false })] }));
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    await userEvent.click(screen.getByRole('button', { name: 'Keep' }));
    expect(screen.getByRole('dialog', { name: 'Swipe summary' })).toBeInTheDocument();
    api.useSwipe.mockReturnValue({});
  });

  it('gives each tab its header, in the platform\'s words, and the gear opens Settings', async () => {
    width(false);
    render(<App platform="android" />);
    expect(screen.getByRole('heading', { name: 'Clean up' })).toBeInTheDocument();
    expect(screen.getByText('Daily swipes, duplicates and similar shots')).toBeInTheDocument();
    const tabs = screen.getByRole('navigation', { name: 'Tabs' });
    await userEvent.click(within(tabs).getByRole('button', { name: 'Organize' }));
    expect(screen.getByText('Sort Phone into folders by place and date')).toBeInTheDocument();
    await userEvent.click(within(tabs).getByRole('button', { name: 'Backup' }));
    expect(screen.getByText('Copies photos and videos to your computer, cloud storage and your own locations')).toBeInTheDocument();
    await userEvent.click(within(tabs).getByRole('button', { name: 'Library' }));
    expect(screen.getByRole('heading', { name: 'Library' })).toBeInTheDocument();

    await userEvent.click(screen.getAllByRole('button', { name: 'Settings' })[0]);
    const settings = screen.getByRole('dialog', { name: 'Settings' });
    await userEvent.click(within(settings).getByRole('radio', { name: 'Dark' }));
    expect(within(settings).getByRole('radio', { name: 'Dark' })).toHaveAttribute('aria-checked', 'true');
    await userEvent.click(within(settings).getByRole('button', { name: 'Back' }));
    expect(screen.queryByRole('dialog', { name: 'Settings' })).not.toBeInTheDocument();
  });

  it('sends what is chosen in Library: Choose photos to send opens Library choosing, and Send there backs them up now', async () => {
    width(false);
    api.useBackup.mockReturnValue({ data: backupState() });
    api.backUpIfDue.mockClear();
    api.startBackup.mockClear();
    render(<App />);
    await waitFor(() => expect(api.backUpIfDue).toHaveBeenCalledOnce());
    const tabs = () => screen.getByRole('navigation', { name: 'Tabs' });
    await userEvent.click(within(tabs()).getByRole('button', { name: 'Backup' }));
    await userEvent.click(screen.getByRole('button', { name: 'Choose photos to send' }));
    // Library, choosing: its selection bar in place of the tabs.
    expect(screen.getByRole('toolbar')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'IMG_1.JPG' }));
    await userEvent.click(within(screen.getByRole('toolbar')).getByRole('button', { name: 'Send' }));
    expect(api.startBackup).toHaveBeenCalledExactlyOnceWith(['IMG_1']);
    expect(notify).toHaveBeenCalledWith('Sending 1 item to Studio iMac.', 'ok');
    expect(screen.getByRole('heading', { name: 'Backup' })).toBeInTheDocument();
    // Turned down by the phone, it says why, and stays where it was.
    api.startBackup.mockRejectedValueOnce(new Error('Pair with a computer first: in Settings, choose Computer.'));
    await userEvent.click(within(tabs()).getByRole('button', { name: 'Library' }));
    await userEvent.click(screen.getByRole('button', { name: 'Select' }));
    await userEvent.click(screen.getByRole('button', { name: 'IMG_2.JPG' }));
    await userEvent.click(within(screen.getByRole('toolbar')).getByRole('button', { name: 'Send' }));
    expect(notify).toHaveBeenLastCalledWith('Pair with a computer first: in Settings, choose Computer.', 'error');
    api.useBackup.mockReturnValue({});
  });

  it('with other locations on, Send asks where to: one of them is sent to and every copy checked, then what was sent offers to delete it', async () => {
    width(false);
    api.useBackup.mockReturnValue({ data: backupState() });
    const archive = location({ id: 's1', kind: 'amazonS3', name: 'Archive', on: true, connected: true, settings: { bucket: 'photos' } });
    let listed = locations({ locations: [...locations().locations, archive] });
    api.useLocations.mockImplementation(() => ({ data: listed }));
    api.startBackup.mockClear();
    api.locationsIfDue.mockClear();
    const { rerender } = render(<App start="library" />);
    await waitFor(() => expect(api.locationsIfDue).toHaveBeenCalledOnce());
    const send = async (name: string) => {
      await userEvent.click(screen.getByRole('button', { name: 'Select' }));
      await userEvent.click(screen.getByRole('button', { name }));
      await userEvent.click(within(screen.getByRole('toolbar')).getByRole('button', { name: 'Send' }));
      return screen.getByRole('dialog', { name: 'Send to' });
    };
    // Put away, nothing is sent.
    await userEvent.click(within(await send('IMG_1.JPG')).getByRole('button', { name: 'Done' }));
    expect(api.sendToLocation).not.toHaveBeenCalled();
    let sheet = await send('IMG_1.JPG');
    await userEvent.click(within(sheet).getByRole('radio', { name: /Archive/ }));
    await userEvent.click(within(sheet).getByRole('button', { name: 'Send 1 item to Archive' }));
    expect(api.sendToLocation).toHaveBeenCalledWith('s1', ['IMG_1']);
    expect(notify).toHaveBeenLastCalledWith('Sending 1 item to Archive. Every copy is checked.', 'ok');
    // Once sent, what was sent, and Done puts it away.
    listed = { ...listed, activity: { task: 'send', stage: 'ready', locations: [], sent: { locationId: 's1', items: 1, matched: 1, bytes: 5 } } };
    rerender(<App start="library" />);
    const sent = await screen.findByRole('dialog', { name: 'Sent' });
    expect(sent).toHaveTextContent('1 item sent to Archive');
    await userEvent.click(within(sent).getByRole('button', { name: 'Done' }));
    expect(screen.queryByRole('dialog', { name: 'Sent' })).not.toBeInTheDocument();
    // The computer chosen, it backs up there as before; the one chosen last is offered first.
    sheet = await send('IMG_2.JPG');
    expect(within(sheet).getByRole('radio', { name: /Archive/ })).toHaveAttribute('aria-checked', 'true');
    await userEvent.click(within(sheet).getByRole('radio', { name: /Studio iMac/ }));
    await userEvent.click(within(sheet).getByRole('button', { name: 'Send 1 item to Studio iMac' }));
    expect(api.startBackup).toHaveBeenCalledWith(['IMG_2']);
    // Refused, it says why.
    api.sendToLocation.mockRejectedValueOnce(new Error('Turn Archive on first.'));
    await userEvent.click(within(screen.getByRole('navigation', { name: 'Tabs' })).getByRole('button', { name: 'Library' }));
    sheet = await send('IMG_2.JPG');
    await userEvent.click(within(sheet).getByRole('radio', { name: /Archive/ }));
    await userEvent.click(within(sheet).getByRole('button', { name: 'Send 1 item to Archive' }));
    expect(notify).toHaveBeenLastCalledWith('Turn Archive on first.', 'error');
    // The Backup tab lists the locations, and Settings their switches.
    await userEvent.click(within(screen.getByRole('navigation', { name: 'Tabs' })).getByRole('button', { name: 'Backup' }));
    expect(within(screen.getByRole('region', { name: 'Locations' })).getByText('Archive')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Choose locations' }));
    expect(within(screen.getByRole('dialog', { name: 'Settings' })).getByRole('switch', { name: 'Archive' })).toBeChecked();
    // A computer not paired yet is paired from its switch.
    listed = locations({ locations: [location({ id: 'computer', kind: 'computer', name: 'Computer' }), archive] });
    rerender(<App start="library" />);
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Settings' })).getByRole('switch', { name: 'Computer' }));
    expect(await screen.findByRole('region', { name: 'Connect your computer' })).toBeInTheDocument();
    api.useLocations.mockReturnValue({});
    api.useBackup.mockReturnValue({});
  });

  it('opens Safe to delete from Library, and once removed says how much was freed; Done goes back to Library', async () => {
    width(false);
    api.useBackup.mockReturnValue({ data: backupState() });
    api.useLibraryBackup.mockReturnValue({ data: { computer: 'Studio iMac', onComputer: ['IMG_1'], waiting: ['IMG_2'], lastChecked: null } });
    api.useSafeReview.mockReturnValue({ data: safeReview() });
    api.removeSafe.mockResolvedValue({ removed: 5998, bytes: 28.1e9, left: 0, storageTotal: 128e9, storageFreeBefore: 16e9 });
    render(<App start="library" />);
    await userEvent.click(within(screen.getByRole('region', { name: 'On Studio iMac' })).getByRole('button', { name: 'Review' }));
    // Back takes it away; opened again, it removes.
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Safe to delete' })).getByRole('button', { name: 'Back' }));
    expect(screen.queryByRole('dialog', { name: 'Safe to delete' })).not.toBeInTheDocument();
    await userEvent.click(within(screen.getByRole('region', { name: 'On Studio iMac' })).getByRole('button', { name: 'Review' }));
    await userEvent.click(screen.getByRole('button', { name: 'Remove 5,998 items' }));
    const freed = await screen.findByRole('dialog', { name: 'Space freed' });
    expect(freed).toHaveTextContent("The copies are on Studio iMac in Pictures/PhotoSense/Alex's iPhone.");
    await userEvent.click(within(freed).getByRole('button', { name: 'Done' }));
    expect(screen.queryByRole('dialog', { name: 'Space freed' })).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Library' })).toBeInTheDocument();
    for (const f of [api.useBackup, api.useLibraryBackup, api.useSafeReview]) f.mockReturnValue({});
  });

  it('after the free plan let only some go, Get Pro opens the Pro sheet; with no computer named, the copies are on the computer', async () => {
    width(false);
    api.usePlan.mockReturnValue({ data: plan() });
    api.useSafeReview.mockReturnValue({ data: safeReview() });
    api.useBackup.mockReturnValue({ data: backupState({ computer: 'Studio iMac' }) });
    api.useLibraryBackup.mockReturnValue({ data: { computer: 'Studio iMac', onComputer: ['IMG_1'], waiting: [], lastChecked: null } });
    api.removeSafe.mockResolvedValue({ removed: 38, bytes: 3e9, left: 5960, storageTotal: 128e9, storageFreeBefore: 16e9 });
    render(<App start="library" />);
    await userEvent.click(screen.getByRole('button', { name: 'Review' }));
    await userEvent.click(screen.getByRole('button', { name: 'Remove 5,998 items' }));
    await userEvent.click(within(screen.getByRole('dialog', { name: 'PhotoSense Pro' })).getByRole('button', { name: 'Remove the 38 largest now' }));
    const freed = await screen.findByRole('dialog', { name: 'Space freed' });
    api.useBackup.mockReturnValue({});
    await userEvent.click(within(freed).getByRole('button', { name: 'Get Pro' }));
    expect(screen.getByRole('dialog', { name: 'PhotoSense Pro' })).toBeInTheDocument();
    for (const f of [api.useBackup, api.useLibraryBackup, api.useSafeReview, api.usePlan]) f.mockReturnValue({});
  });

  it('names the computer in Settings once the phone has one, and pairs one from there when it has none', async () => {
    width(false);
    api.useBackup.mockReturnValue({ data: backupState() });
    const { unmount } = render(<App />);
    await userEvent.click(screen.getAllByRole('button', { name: 'Settings' })[0]);
    expect(within(screen.getByRole('region', { name: 'Computer' })).getAllByRole('listitem').map(li => li.textContent)).toEqual(['Studio iMac Paired', "Backup folder Pictures/PhotoSense/Alex's iPhone"]);
    unmount();
    api.useBackup.mockReturnValue({ data: backupState({ stage: 'unpaired', computer: null }) });
    render(<App />);
    await userEvent.click(screen.getAllByRole('button', { name: 'Settings' })[0]);
    await userEvent.click(screen.getByRole('button', { name: 'Connect your computer' }));
    expect(screen.getByRole('region', { name: 'Connect your computer' })).toBeInTheDocument();
    api.useBackup.mockReturnValue({});
  });

  it('pairs a computer from Backup when it has none, and Not now puts pairing away', async () => {
    width(false);
    api.useBackup.mockReturnValue({ data: backupState({ stage: 'unpaired', computer: null }) });
    render(<App start="backup" />);
    await userEvent.click(screen.getByRole('button', { name: 'Connect your computer' }));
    const pairing = screen.getByRole('region', { name: 'Connect your computer' });
    expect(within(pairing).queryByRole('progressbar')).not.toBeInTheDocument();
    await userEvent.click(within(pairing).getByRole('button', { name: 'Not now' }));
    expect(screen.queryByRole('region', { name: 'Connect your computer' })).not.toBeInTheDocument();
    // Back puts it away too.
    await userEvent.click(screen.getByRole('button', { name: 'Connect your computer' }));
    await act(async () => { history.back(); await new Promise(r => setTimeout(r, 20)); });
    expect(screen.queryByRole('region', { name: 'Connect your computer' })).not.toBeInTheDocument();
    api.useBackup.mockReturnValue({});
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

    it('opens Subscription from the plan, and goes back to Settings from it, and away by Back', async () => {
      api.usePlan.mockReturnValue({ data: plan({ tier: 'trial', kind: 'yearly', started: '2098-12-01T00:00:00Z', until: '2099-01-10T00:00:00Z', leftToday: null }) });
      render(<App />);
      await userEvent.click(within(await gear()).getByRole('button', { name: 'PhotoSense Pro trial. Subscription' }));
      const page = screen.getByRole('dialog', { name: 'Subscription' });
      expect(screen.queryByRole('dialog', { name: 'Settings' })).not.toBeInTheDocument();
      // The reminder is the phone's own notification, kept by the service.
      expect(within(page).getByRole('switch', { name: /^Remind me 2 days before it ends/ })).not.toBeChecked();
      api.changeNotices.mockResolvedValueOnce({ lowStorage: false, trialReminder: true, allowed: true });
      await userEvent.click(within(page).getByRole('switch', { name: /^Remind me 2 days before it ends/ }));
      expect(api.changeNotices).toHaveBeenLastCalledWith({ trialReminder: true });
      await userEvent.click(within(page).getByRole('button', { name: 'Terms' }));
      await userEvent.click(within(page).getByRole('button', { name: 'Privacy policy' }));
      expect(onAbout.mock.calls).toEqual([['terms'], ['privacy']]);
      await userEvent.click(within(page).getByRole('button', { name: 'Back' }));
      expect(screen.getByRole('dialog', { name: 'Settings' })).toBeInTheDocument();
      await act(async () => { history.back(); await new Promise(r => setTimeout(r, 20)); });
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      api.usePlan.mockReturnValue({});
    });

    it('keeps Remove location when sharing in the page\'s settings', async () => {
      render(<App />);
      await userEvent.click(within(await gear()).getByRole('switch', { name: /^Remove location when sharing/ }));
      expect(onSettings).toHaveBeenCalledExactlyOnceWith({ removeLocationWhenSharing: false });
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
      settings = { eraseAskTwice: false, distanceUnit: 'miles', phonesSeen: false, removeLocationWhenSharing: true };
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

      await userEvent.click(within(await gear()).getByRole('button', { name: 'Privacy policy' }));
      await userEvent.click(within(await gear()).getByRole('button', { name: 'About PhotoSense 1.4.0' }));
      expect(onAbout.mock.calls).toEqual([['privacy'], []]);
      await userEvent.click(within(await gear()).getByRole('radio', { name: 'Kilometers' }));
      expect(onSettings).toHaveBeenLastCalledWith({ distanceUnit: 'km' });
    });
  });

  it('pushes a list from Find, and a review over the list, on a phone, without the tabs, and Back takes each back', async () => {
    width(false);
    render(<App />);
    await openDuplicates();
    expect(screen.getByRole('heading', { name: 'Duplicates' })).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Tabs' })).not.toBeInTheDocument();
    await userEvent.click(within(screen.getByRole('list', { name: 'Groups' })).getByRole('button', { name: /IMG_4299\.JPG/ }));
    expect(screen.getByRole('region', { name: 'Review' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Duplicates' })).not.toBeInTheDocument();
    await act(async () => { history.back(); await new Promise(r => setTimeout(r, 20)); });
    expect(screen.getByRole('heading', { name: 'Duplicates' })).toBeInTheDocument();
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

  it('shows a review beside the list on a tablet, so nothing is pushed, and tapping the tab goes back to Clean up\'s first screen', async () => {
    width(true);
    render(<App start="cleanup" />);
    await openDuplicates();
    expect(screen.getAllByRole('heading', { name: 'Duplicates' })).toHaveLength(1);
    expect(screen.getByRole('heading', { name: 'IMG_4299.JPG' })).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Tabs' })).toBeInTheDocument();
    await userEvent.click(within(screen.getByRole('navigation', { name: 'Tabs' })).getByRole('button', { name: /^Clean up/ }));
    expect(screen.getByRole('heading', { name: 'Clean up' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'IMG_4299.JPG' })).not.toBeInTheDocument();
  });

  it('opens Year in review from its card in Library, and the share card from what compressing saved', async () => {
    width(false);
    api.useYear.mockReturnValue({ data: { year: 2026, photos: 3, videos: 0, trips: 0, topTrips: [], months: Array(12).fill(0), busiestMonth: 0, busiestPlace: null, freed: 0, removed: 0, longestStreak: 0, sessions: 0 } });
    let compressing: unknown = { stage: 'running', quality: 'balanced', total: 1, done: 0, current: 'IMG_1.MOV', progress: 0, results: [], saved: 0, originalsDeleted: null, problem: null };
    api.useCompression.mockImplementation(() => ({ data: compressing }));
    const { rerender } = render(<App start="library" />);
    await userEvent.click(within(screen.getByRole('region', { name: 'Year in review' })).getByRole('button', { name: 'Watch' }));
    const stories = screen.getByRole('dialog', { name: 'Year in review' });
    await userEvent.click(within(stories).getByRole('button', { name: 'Close' }));
    expect(screen.queryByRole('dialog', { name: 'Year in review' })).not.toBeInTheDocument();

    compressing = { stage: 'done', quality: 'balanced', total: 1, done: 1, current: null, progress: 1, results: [{ id: 'v', fileName: 'IMG_1.MOV', before: 900 * 1024 ** 2, after: 120 * 1024 ** 2 }],
      saved: 780 * 1024 ** 2, originalsDeleted: true, problem: null };
    rerender(<App start="library" />);
    await waitFor(() => expect(notify).toHaveBeenCalledWith('Saved 780.0 MB. The original is in Recently Deleted for 30 days.', 'ok', expect.objectContaining({ label: 'Share' })));
    await act(async () => { notify.mock.calls.find(c => c[2]?.label === 'Share')![2].run(); });
    const sheet = screen.getByRole('dialog', { name: 'Share your result' });
    expect(within(sheet).getByRole('figure', { name: 'Share card' })).toHaveTextContent('780.0 MBfreed on my iPhone1 item cleaned up');
    await userEvent.click(within(sheet).getByRole('button', { name: 'Not now' }));
    expect(screen.queryByRole('dialog', { name: 'Share your result' })).not.toBeInTheDocument();
    api.useYear.mockReturnValue({});
    api.useCompression.mockReturnValue({});
  });

  it('turns low-storage alerts on from Clean up and off from Settings', async () => {
    width(false);
    api.useNotices.mockReturnValue({ data: { lowStorage: false, trialReminder: false, allowed: true } });
    api.changeNotices.mockResolvedValue({ lowStorage: true, trialReminder: false, allowed: true });
    render(<App start="cleanup" />);
    await userEvent.click(screen.getByRole('switch', { name: /^Low-storage alerts/ }));
    expect(api.changeNotices).toHaveBeenLastCalledWith({ lowStorage: true });
    api.useNotices.mockReturnValue({ data: { lowStorage: true, trialReminder: false, allowed: true } });
    await userEvent.click(screen.getAllByRole('button', { name: 'Settings' })[0]);
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Settings' })).getByRole('switch', { name: /^Low-storage alerts/ }));
    expect(api.changeNotices).toHaveBeenLastCalledWith({ lowStorage: false });
    api.useNotices.mockReturnValue({});
  });

  it('opens what a notification tapped is about: Free up space, or the subscription, as it opens or comes back; and says why it could not ask', async () => {
    width(false);
    api.openedNotice.mockResolvedValueOnce({ opens: 'free-up-space' });
    const first = render(<App start="library" />);
    expect(await screen.findByRole('heading', { level: 1, name: 'Free up space' })).toBeInTheDocument();
    first.unmount();

    api.usePlan.mockReturnValue({ data: plan({ tier: 'trial', kind: 'yearly', until: '2099-01-10T00:00:00Z', leftToday: null }) });
    render(<App start="library" />);
    await act(async () => { await Promise.resolve(); });
    api.openedNotice.mockResolvedValueOnce({ opens: 'subscription' });
    await act(async () => { window.dispatchEvent(new Event('photosense-notice')); });
    expect(await screen.findByRole('dialog', { name: 'Subscription' })).toBeInTheDocument();

    api.openedNotice.mockRejectedValueOnce(new Error('The phone did not answer.'));
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'visible' });
    await act(async () => { document.dispatchEvent(new Event('visibilitychange')); });
    await waitFor(() => expect(notify).toHaveBeenCalledWith('The phone did not answer.', 'error'));
    // Hidden, it does not ask.
    const asked = api.openedNotice.mock.calls.length;
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'hidden' });
    await act(async () => { document.dispatchEvent(new Event('visibilitychange')); });
    expect(api.openedNotice).toHaveBeenCalledTimes(asked);
  });

  it("opens what a widget or the app icon's menu asks: a backup started, today's swipe set, or Search; and says why a backup could not start", async () => {
    width(false);
    api.useSwipe.mockImplementation(() => ({ data: swipeState({ set: [swipeCard('a')] }) }));
    api.useBackup.mockReturnValue({ data: backupState() });
    const tell = (opens: string) => {
      api.openedNotice.mockResolvedValueOnce({ opens });
      return act(async () => { window.dispatchEvent(new Event('photosense-notice')); });
    };
    render(<App start="library" />);
    await act(async () => { await Promise.resolve(); });
    api.startBackup.mockClear();
    await tell('backup');
    await waitFor(() => expect(api.startBackup).toHaveBeenCalledOnce());
    expect(screen.getByRole('heading', { level: 1, name: 'Backup' })).toBeInTheDocument();

    await tell('swipe');
    const swiping = await screen.findByRole('dialog', { name: 'Swipe to clean' });
    await userEvent.click(within(swiping).getByRole('button', { name: 'Close' }));

    await tell('search');
    expect(await screen.findByRole('dialog', { name: 'Search' })).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');

    // A backup that cannot start says why.
    api.startBackup.mockRejectedValueOnce(new Error('A backup to Studio iMac is running.'));
    await tell('backup');
    await waitFor(() => expect(notify).toHaveBeenCalledWith('A backup to Studio iMac is running.', 'error'));

    // With no computer, Backup only opens, to pair one; with today's set through, Clean up only.
    api.startBackup.mockClear();
    api.useBackup.mockReturnValue({ data: backupState({ computer: null }) });
    api.useSwipe.mockImplementation(() => ({ data: swipeState({ set: [swipeCard('a', { delete: false })] }) }));
    // Drawn again, as the phone's answers changed.
    await userEvent.click(within(screen.getByRole('navigation', { name: 'Tabs' })).getByRole('button', { name: /^Library/ }));
    await tell('backup');
    await tell('swipe');
    expect(screen.getByRole('heading', { level: 1, name: 'Clean up' })).toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'Swipe to clean' })).not.toBeInTheDocument();
    expect(api.startBackup).not.toHaveBeenCalled();
    api.useSwipe.mockReturnValue({});
    api.useBackup.mockReturnValue({});
  });

  it("pushes a category opened from Find over Clean up's first screen, which Back takes back; a tablet shows it beside the list", async () => {
    width(false);
    const { unmount } = render(<App start="cleanup" />);
    const screenshots = () => userEvent.click(within(screen.getByRole('region', { name: 'Find' })).getByRole('button', { name: /^Screenshots/ }));
    await screenshots();
    expect(screen.getByRole('heading', { level: 1, name: 'Screenshots' })).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Tabs' })).not.toBeInTheDocument();
    await act(async () => { history.back(); await new Promise(r => setTimeout(r, 20)); });
    expect(screen.getByRole('heading', { name: 'Clean up' })).toBeInTheDocument();
    unmount();
    width(true);
    render(<App start="cleanup" />);
    await screenshots();
    expect(screen.getByRole('heading', { name: 'Clean up' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Screenshots' })).toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'Find' })).getByRole('button', { name: /^Screenshots/ })).toHaveAttribute('aria-current', 'true');
  });
});
