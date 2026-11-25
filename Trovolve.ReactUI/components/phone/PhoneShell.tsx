import React, { useMemo, useState } from 'react';
import { appIconUrl } from '../../lib/apiClient';
import { PhoneContext, useForm, WORDS, type Collection, type Foot, type PhoneFrame, type Place, type Platform, type TabId } from '../../lib/phone';
import type { Toast } from '../Toaster';
import { BackupIcon, CleanUpIcon, FixIcon, GearIcon, OrganizeIcon, PlacesIcon, SidebarIcon, TimelineIcon, VaultIcon } from './Icons';
import { PlaceContext } from './Screens';
import { PhoneToaster } from './PhoneToaster';

/** The four tabs, in their order, with their names and icons. */
export const TABS: readonly { readonly id: TabId; readonly name: string; readonly Icon: (p: { size?: number }) => React.ReactElement }[] = [
  { id: 'library', name: 'Library', Icon: TimelineIcon },
  { id: 'cleanup', name: 'Clean up', Icon: CleanUpIcon },
  { id: 'organize', name: 'Organize', Icon: OrganizeIcon },
  { id: 'backup', name: 'Backup', Icon: BackupIcon },
];

/** Library's collections, listed on a tablet under the tabs, with their names and icons. */
export const COLLECTIONS: readonly { readonly id: Collection; readonly name: string; readonly Icon: (p: { size?: number }) => React.ReactElement }[] = [
  { id: 'trips', name: 'Trips', Icon: PlacesIcon },
  { id: 'fixes', name: 'Needs fixing', Icon: FixIcon },
  { id: 'hidden', name: 'Hidden', Icon: VaultIcon },
];

interface Props {
  readonly platform: Platform;
  readonly tab: TabId;
  /** What stands at the foot on a phone: the tab bar, a selection's bar, or nothing while a screen is pushed. */
  readonly foot: Foot;
  /** How many each tab and collection holds, said beside its name in an iPad's sidebar. */
  readonly counts: Partial<Record<TabId | Collection, string>>;
  /** What a tablet shows in place of the tab: one of Library's collections, or Settings. */
  readonly place?: Place;
  /** Settings, as a tablet shows it in place of the tab. */
  readonly placeScreen?: React.ReactNode;
  /** Shows a collection or Settings on a tablet, from its sidebar or rail. */
  onPlace?(place: Place): void;
  /** Each tab's screens. A tab is drawn once first turned to, and kept with all it holds while another is shown. */
  readonly screens: Record<TabId, React.ReactNode>;
  readonly toasts: Toast[];
  /** Something the person should know above every screen, such as the service not answering. */
  readonly notice?: React.ReactNode;
  /** A sheet open over the screens, such as Settings. */
  readonly sheet?: React.ReactNode;
  /** A screen of its own over the tabs, such as Welcome or Scanning; the tabs wait behind it, out of reach. */
  readonly cover?: React.ReactElement | null;
  /** Turns to a tab; turning to the one showing goes back to its first screen. */
  onTab(tab: TabId): void;
  onSettings(): void;
  removeToast(id: number): void;
}

/**
 * The phone and tablet layout's frame: the tabs (a floating glass bar on an iPhone, a navigation bar on Android, a
 * sidebar on an iPad, a rail on an Android tablet), the tab's screens, and its messages. It tells every screen inside
 * where it is through `usePhone`.
 */
export function PhoneShell({ platform, tab, foot: asked, counts, place: placed, placeScreen, screens, toasts, notice, sheet, cover, onTab, onPlace, onSettings, removeToast }: Props) {
  const form = useForm();
  const [sidebarHidden, setSidebarHidden] = useState(false);
  const [slot, setSlot] = useState<HTMLElement | null>(null);
  const ios = platform === 'ios';
  const tablet = form === 'tablet';
  // A phone has no room beside its tabs: what a tablet shows in place of one, a phone shows over them all.
  const place = tablet ? placed : undefined;
  const [seen, setSeen] = useState<ReadonlySet<TabId>>(new Set([tab]));
  // Library draws its collections, so it is drawn, out of sight, while one shows.
  const needed = place && place !== 'settings' ? 'library' : tab;
  if (!seen.has(tab) || !seen.has(needed)) setSeen(new Set([...seen, tab, needed]));

  const foot = cover ? 'none' : asked;
  const frame = useMemo<PhoneFrame>(() => ({
    platform, form, ios, tablet, words: WORDS[platform], sidebarHidden: ios && tablet && sidebarHidden, foot: tablet ? 'tabs' : foot, openSettings: onSettings,
    place, placeSlot: place ? slot : null,
  }), [platform, form, ios, tablet, sidebarHidden, foot, onSettings, place, slot]);

  const bars = !tablet && foot === 'tabs';
  return (
    <PhoneContext.Provider value={frame}>
      {/* Android draws its own status and gesture bars over the page's edges: the page paints under them. */}
      {!ios && <div aria-hidden className="fixed inset-x-0 top-0 z-30 h-[var(--safe-t)] bg-bg" />}
      {!ios && <div aria-hidden className={`fixed inset-x-0 bottom-0 z-30 h-[var(--safe-b)] ${!tablet && foot !== 'none' ? 'bg-bar' : 'bg-bg'}`} />}
      <div inert={!!cover} className={`fixed inset-x-0 flex overflow-hidden bg-bg font-ui text-t1 ${ios ? 'inset-y-0' : 'bottom-[var(--safe-b)] top-[var(--safe-t)]'}`}>
        {ios && tablet && !sidebarHidden && <Sidebar tab={tab} place={place} counts={counts} onTab={onTab} onPlace={p => onPlace?.(p)} onHide={() => setSidebarHidden(true)} />}
        {!ios && tablet && <Rail tab={tab} place={place} onTab={onTab} onPlace={p => onPlace?.(p)} />}
        <main className="relative flex min-w-0 flex-1 flex-col">
          {ios && tablet && sidebarHidden && <FloatingTabs tab={place ? undefined : tab} onTab={onTab} onShow={() => setSidebarHidden(false)} />}
          {notice}
          {TABS.map(({ id }) => seen.has(id) && (
            <div key={id} className={id === tab && !place ? 'flex min-h-0 min-w-0 flex-1' : 'hidden'}>{screens[id]}</div>
          ))}
          {/* A collection is drawn here by Library; Settings by the app. */}
          {place && <div ref={setSlot} className="flex min-h-0 min-w-0 flex-1"><PlaceContext.Provider value>{place === 'settings' && placeScreen}</PlaceContext.Provider></div>}
        </main>
      </div>
      {bars && (ios ? <GlassTabBar tab={tab} onTab={onTab} /> : <NavigationBar tab={tab} onTab={onTab} />)}
      {cover}
      {sheet}
      <PhoneToaster toasts={toasts} remove={removeToast} />
    </PhoneContext.Provider>
  );
}

interface TabsProps {
  /** The tab showing; none while a tablet shows a collection or Settings in its place. */
  readonly tab?: TabId;
  onTab(tab: TabId): void;
}

interface PlacesProps extends TabsProps {
  readonly place?: Place;
  onPlace(place: Place): void;
}

/** The iPhone's tabs: a floating capsule of glass, the tab showing lit in a pill of its own. */
function GlassTabBar({ tab, onTab }: TabsProps) {
  return (
    <nav aria-label="Tabs" className="fixed inset-x-4 bottom-[22px] z-20 grid h-16 grid-cols-4 rounded-full bg-bar p-1 shadow-bar [border:var(--bar-bd)] backdrop-blur-[24px] backdrop-saturate-[1.8]">
      {TABS.map(({ id, name, Icon }) => (
        <button key={id} type="button" aria-current={id === tab ? 'page' : undefined} onClick={() => onTab(id)}
          className={`flex flex-col items-center justify-center gap-[2px] rounded-full transition-colors duration-200 ${id === tab ? 'bg-bar-hi text-bar-on' : 'text-bar-off'}`}>
          <Icon />
          <span className="whitespace-nowrap text-[10.5px] font-semibold">{name}</span>
        </button>
      ))}
    </nav>
  );
}

/** Android's navigation bar: across the foot, each tab's icon in an indicator that fills for the tab showing. */
function NavigationBar({ tab, onTab }: TabsProps) {
  return (
    <nav aria-label="Tabs" className="fixed inset-x-0 bottom-[var(--safe-b)] z-20 grid h-20 grid-cols-4 bg-bar pt-3">
      {TABS.map(({ id, name, Icon }) => <MaterialTab key={id} name={name} Icon={Icon} on={id === tab} onClick={() => onTab(id)} />)}
    </nav>
  );
}

/**
 * Android's rail on a tablet: down the left, the app's icon above the tabs, Library's collections under a line, and
 * Settings at the foot.
 */
function Rail({ tab, place, onTab, onPlace }: PlacesProps) {
  return (
    <nav aria-label="Tabs" className="flex w-24 shrink-0 flex-col items-center gap-[14px] overflow-y-auto bg-rail pb-4 pt-6">
      <img src={appIconUrl} alt="" className="mb-4 h-9 w-9 rounded-[10px]" />
      {TABS.map(({ id, name, Icon }) => <MaterialTab key={id} name={name} Icon={Icon} on={!place && id === tab} onClick={() => onTab(id)} />)}
      <span aria-hidden className="h-px w-12 shrink-0 bg-sep" />
      {COLLECTIONS.map(({ id, name, Icon }) => <MaterialTab key={id} name={name} Icon={Icon} on={place === id} onClick={() => onPlace(id)} />)}
      <span className="mt-auto"><MaterialTab name="Settings" Icon={GearIcon} on={place === 'settings'} onClick={() => onPlace('settings')} /></span>
    </nav>
  );
}

interface MaterialTabProps {
  readonly name: string;
  readonly Icon: (p: { size?: number }) => React.ReactElement;
  readonly on: boolean;
  onClick(): void;
}

/** One of Android's tabs: its icon in a 56 × 32 indicator, its name under it. */
function MaterialTab({ name, Icon, on, onClick }: MaterialTabProps) {
  return (
    <button type="button" aria-current={on ? 'page' : undefined} onClick={onClick} className="flex w-20 flex-col items-center gap-1 justify-self-center">
      <span className={`grid h-8 w-14 place-items-center rounded-2xl transition-colors duration-200 ${on ? 'bg-bar-hi text-bar-on' : 'text-bar-off'}`}><Icon /></span>
      <span className={`text-[12px] ${on ? 'font-bold text-t1' : 'font-medium text-bar-off'}`}>{name}</span>
    </button>
  );
}

interface SidebarProps extends PlacesProps {
  readonly counts: Partial<Record<TabId | Collection, string>>;
  onHide(): void;
}

interface SideRowProps {
  readonly name: string;
  readonly Icon: (p: { size?: number }) => React.ReactElement;
  readonly on: boolean;
  readonly count?: string;
  onClick(): void;
}

/** One row of an iPad's sidebar: its icon, its name, and how many it holds; lit while it shows. */
function SideRow({ name, Icon, on, count, onClick }: SideRowProps) {
  return (
    <button type="button" aria-current={on ? 'page' : undefined} onClick={onClick}
      className={`flex h-[46px] shrink-0 items-center gap-3 rounded-full px-[14px] text-[16px] ${on ? 'bg-bar-hi font-semibold text-bar-on' : 'font-medium text-t1'}`}>
      <Icon size={22} />
      <span className="flex-1 text-left">{name}</span>
      {count && ' '}
      {count && <span className="text-[13px] font-normal tabular-nums text-t2">{count}</span>}
    </button>
  );
}

/**
 * An iPad's sidebar: a floating glass panel with the app's name, each tab with how many it holds, Library's collections
 * under their own heading, and Settings at its foot.
 */
function Sidebar({ tab, place, counts, onTab, onPlace, onHide }: SidebarProps) {
  return (
    <div className="flex w-[300px] shrink-0 animate-fade-in pb-[14px] pl-[14px] pt-[calc(var(--safe-t)+6px)]">
      <nav aria-label="Tabs" className="flex flex-1 flex-col gap-[2px] rounded-[26px] bg-side p-[10px] shadow-bar [border:var(--bar-bd)] backdrop-blur-[24px]">
        <div className="flex h-[50px] items-center gap-[10px] pb-1.5 pl-2.5 pr-0.5">
          <img src={appIconUrl} alt="" className="h-7 w-7 rounded-lg" />
          <span className="flex-1 font-title text-[19px] font-bold">Trovolve</span>
          <button type="button" aria-label="Hide sidebar" title="Hide sidebar" onClick={onHide} className="grid h-10 w-10 place-items-center rounded-full text-t1"><SidebarIcon /></button>
        </div>
        {TABS.map(({ id, name, Icon }) => <SideRow key={id} name={name} Icon={Icon} on={!place && id === tab} count={counts[id]} onClick={() => onTab(id)} />)}
        <h2 className="px-[14px] pb-1 pt-4 text-[13px] font-semibold text-t2">Collections</h2>
        {COLLECTIONS.map(({ id, name, Icon }) => <SideRow key={id} name={name} Icon={Icon} on={place === id} count={counts[id]} onClick={() => onPlace(id)} />)}
        <span className="mt-auto" />
        <SideRow name="Settings" Icon={GearIcon} on={place === 'settings'} onClick={() => onPlace('settings')} />
      </nav>
    </div>
  );
}

/** With an iPad's sidebar hidden: a round button to bring it back, and the tabs in a floating glass bar at the top. */
function FloatingTabs({ tab, onTab, onShow }: TabsProps & { onShow(): void }) {
  const glass = 'bg-bar shadow-bar [border:var(--bar-bd)] backdrop-blur-[24px]';
  return (
    <>
      <button type="button" aria-label="Show sidebar" title="Show sidebar" onClick={onShow}
        className={`absolute left-4 top-[calc(var(--safe-t)+8px)] z-[22] grid h-11 w-11 place-items-center rounded-full text-t1 ${glass}`}><SidebarIcon /></button>
      <nav aria-label="Tabs" className={`absolute left-1/2 top-[calc(var(--safe-t)+8px)] z-[22] flex h-11 -translate-x-1/2 gap-[2px] rounded-full p-1 ${glass}`}>
        {TABS.map(({ id, name }) => (
          <button key={id} type="button" aria-current={id === tab ? 'page' : undefined} onClick={() => onTab(id)}
            className={`h-9 whitespace-nowrap rounded-full px-[18px] text-[15px] font-semibold ${id === tab ? 'bg-bar-hi text-bar-on' : 'text-t1'}`}>{name}</button>
        ))}
      </nav>
    </>
  );
}
