import React, { useMemo, useState } from 'react';
import { appIconUrl } from '../../lib/apiClient';
import { PhoneContext, useForm, WORDS, type Foot, type PhoneFrame, type Platform, type TabId } from '../../lib/phone';
import type { Toast } from '../Toaster';
import { CleanUpIcon, FolderIcon, LibraryIcon, SendIcon, SidebarIcon } from './Icons';
import { PhoneToaster } from './PhoneToaster';

/** The four tabs, in their order, with their names and icons. */
export const TABS: readonly { readonly id: TabId; readonly name: string; readonly Icon: (p: { size?: number }) => React.ReactElement }[] = [
  { id: 'library', name: 'Library', Icon: LibraryIcon },
  { id: 'cleanup', name: 'Clean up', Icon: CleanUpIcon },
  { id: 'organize', name: 'Organize', Icon: FolderIcon },
  { id: 'send', name: 'Send', Icon: SendIcon },
];

interface Props {
  readonly platform: Platform;
  readonly tab: TabId;
  /** What stands at the foot on a phone: the tab bar, a selection's bar, or nothing while a screen is pushed. */
  readonly foot: Foot;
  /** How many each tab holds, said beside its name in an iPad's sidebar. */
  readonly counts: Partial<Record<TabId, string>>;
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
export function PhoneShell({ platform, tab, foot: asked, counts, screens, toasts, notice, sheet, cover, onTab, onSettings, removeToast }: Props) {
  const form = useForm();
  const [sidebarHidden, setSidebarHidden] = useState(false);
  const [seen, setSeen] = useState<ReadonlySet<TabId>>(new Set([tab]));
  if (!seen.has(tab)) setSeen(new Set([...seen, tab]));

  const ios = platform === 'ios';
  const tablet = form === 'tablet';
  const foot = cover ? 'none' : asked;
  const frame = useMemo<PhoneFrame>(() => ({
    platform, form, ios, tablet, words: WORDS[platform], sidebarHidden: ios && tablet && sidebarHidden, foot: tablet ? 'tabs' : foot, openSettings: onSettings,
  }), [platform, form, ios, tablet, sidebarHidden, foot, onSettings]);

  const bars = !tablet && foot === 'tabs';
  return (
    <PhoneContext.Provider value={frame}>
      {/* Android draws its own status and gesture bars over the page's edges: the page paints under them. */}
      {!ios && <div aria-hidden className="fixed inset-x-0 top-0 z-30 h-[var(--safe-t)] bg-bg" />}
      {!ios && <div aria-hidden className={`fixed inset-x-0 bottom-0 z-30 h-[var(--safe-b)] ${!tablet && foot !== 'none' ? 'bg-bar' : 'bg-bg'}`} />}
      <div inert={!!cover} className={`fixed inset-x-0 flex overflow-hidden bg-bg font-ui text-t1 ${ios ? 'inset-y-0' : 'bottom-[var(--safe-b)] top-[var(--safe-t)]'}`}>
        {ios && tablet && !sidebarHidden && <Sidebar tab={tab} counts={counts} onTab={onTab} onHide={() => setSidebarHidden(true)} />}
        {!ios && tablet && <Rail tab={tab} onTab={onTab} />}
        <main className="relative flex min-w-0 flex-1 flex-col">
          {ios && tablet && sidebarHidden && <FloatingTabs tab={tab} onTab={onTab} onShow={() => setSidebarHidden(false)} />}
          {notice}
          {TABS.map(({ id }) => seen.has(id) && (
            <div key={id} className={id === tab ? 'flex min-h-0 min-w-0 flex-1' : 'hidden'}>{screens[id]}</div>
          ))}
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
  readonly tab: TabId;
  onTab(tab: TabId): void;
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
      {TABS.map(({ id, name, Icon }) => <MaterialTab key={id} id={id} name={name} Icon={Icon} on={id === tab} onTab={onTab} />)}
    </nav>
  );
}

/** Android's rail on a tablet: down the left, the app's icon above the tabs. */
function Rail({ tab, onTab }: TabsProps) {
  return (
    <nav aria-label="Tabs" className="flex w-24 shrink-0 flex-col items-center gap-[14px] bg-rail pt-6">
      <img src={appIconUrl} alt="" className="mb-4 h-9 w-9 rounded-[10px]" />
      {TABS.map(({ id, name, Icon }) => <MaterialTab key={id} id={id} name={name} Icon={Icon} on={id === tab} onTab={onTab} />)}
    </nav>
  );
}

interface MaterialTabProps {
  readonly id: TabId;
  readonly name: string;
  readonly Icon: (p: { size?: number }) => React.ReactElement;
  readonly on: boolean;
  onTab(tab: TabId): void;
}

/** One of Android's tabs: its icon in a 56 × 32 indicator, its name under it. */
function MaterialTab({ id, name, Icon, on, onTab }: MaterialTabProps) {
  return (
    <button type="button" aria-current={on ? 'page' : undefined} onClick={() => onTab(id)} className="flex w-20 flex-col items-center gap-1 justify-self-center">
      <span className={`grid h-8 w-14 place-items-center rounded-2xl transition-colors duration-200 ${on ? 'bg-bar-hi text-bar-on' : 'text-bar-off'}`}><Icon /></span>
      <span className={`text-[12px] ${on ? 'font-bold text-t1' : 'font-medium text-bar-off'}`}>{name}</span>
    </button>
  );
}

interface SidebarProps extends TabsProps {
  readonly counts: Partial<Record<TabId, string>>;
  onHide(): void;
}

/** An iPad's sidebar: a floating glass panel with the app's name, each tab with how many it holds, and a note at its foot. */
function Sidebar({ tab, counts, onTab, onHide }: SidebarProps) {
  return (
    <div className="flex w-[300px] shrink-0 animate-fade-in pb-[14px] pl-[14px] pt-[calc(var(--safe-t)+6px)]">
      <nav aria-label="Tabs" className="flex flex-1 flex-col gap-[2px] rounded-[26px] bg-side p-[10px] shadow-bar [border:var(--bar-bd)] backdrop-blur-[24px]">
        <div className="flex h-[50px] items-center gap-[10px] pb-1.5 pl-2.5 pr-0.5">
          <img src={appIconUrl} alt="" className="h-7 w-7 rounded-lg" />
          <span className="flex-1 font-title text-[19px] font-bold">PhotoSense</span>
          <button type="button" aria-label="Hide sidebar" title="Hide sidebar" onClick={onHide} className="grid h-10 w-10 place-items-center rounded-full text-t1"><SidebarIcon /></button>
        </div>
        {TABS.map(({ id, name, Icon }) => (
          <button key={id} type="button" aria-current={id === tab ? 'page' : undefined} onClick={() => onTab(id)}
            className={`flex h-[46px] items-center gap-3 rounded-full px-[14px] text-[16px] ${id === tab ? 'bg-bar-hi font-semibold text-bar-on' : 'font-medium text-t1'}`}>
            <Icon size={22} />
            <span className="flex-1 text-left">{name}</span>
            {counts[id] && ' '}
            {counts[id] && <span className="text-[13px] font-normal tabular-nums text-t2">{counts[id]}</span>}
          </button>
        ))}
        <p className="mt-auto px-[14px] py-[10px] text-[13px] leading-[1.4] text-t2">Runs on this iPad. Nothing is uploaded.</p>
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
