import React from 'react';
import { usePhone } from '../../lib/phone';
import { HeaderTextButton, IconButton } from './Controls';
import { ArrowBackIcon, ChevronBackIcon, CloseIcon, GearIcon } from './Icons';

// On an iPhone or iPad the screen runs under the status bar, so a header starts below it, and lower still on an iPad
// whose sidebar is hidden, where the floating bar of tabs takes the top. On Android the page starts below the bar.
function useHeaderTop() {
  const { ios, sidebarHidden } = usePhone();
  if (!ios) return '';
  return sidebarHidden ? 'pt-[calc(var(--safe-t)+62px)]' : 'pt-[var(--safe-t)]';
}

/** The gear that opens Settings, at the top right of every tab's first screen. */
export function GearButton() {
  const { openSettings, ios } = usePhone();
  return <IconButton label="Settings" onClick={openSettings}><GearIcon size={ios ? 21 : 22} /></IconButton>;
}

interface RootHeaderProps {
  readonly title: string;
  readonly subtitle?: string;
  /** A word in the header, such as Select or Cancel. */
  readonly textAction?: { readonly label: string; run(): void };
  /** Android puts a cross before the title, instead of Cancel, to stop selecting. */
  readonly onStop?: () => void;
  /** Round buttons before the gear, such as the map. */
  readonly buttons?: React.ReactNode;
  /** Leaves out the gear: on a tablet the detail beside the list carries it. */
  readonly noGear?: boolean;
}

/**
 * The header of a tab's first screen. On an iPhone a row of round glass buttons over a large title; on Android a bar
 * with the title on the left and its buttons on the right; either way the subtitle under it.
 */
export function RootHeader({ title, subtitle, textAction, onStop, buttons, noGear }: RootHeaderProps) {
  const { ios } = usePhone();
  const top = useHeaderTop();
  const text = textAction && <HeaderTextButton onClick={textAction.run}>{textAction.label}</HeaderTextButton>;
  if (ios) {
    return (
      <header className={`flex flex-col gap-0.5 px-4 pb-3 ${top}`}>
        <div className="flex h-[50px] items-center justify-end gap-2">{text}{buttons}{!noGear && <GearButton />}</div>
        <h1 className="font-title text-[length:var(--h1)] font-bold leading-[1.15] tracking-[.01em]">{title}</h1>
        {subtitle && <p className="text-[length:var(--fs-sub)] text-t2">{subtitle}</p>}
      </header>
    );
  }
  return (
    <header>
      <div className="flex h-16 items-center gap-[2px] pl-4 pr-1">
        {onStop && <span className="-ml-3"><IconButton label="Stop selecting" onClick={onStop}><CloseIcon /></IconButton></span>}
        <h1 className="min-w-0 flex-1 truncate font-title text-[length:var(--h1)] font-normal">{title}</h1>
        {text}{buttons}{!noGear && <GearButton />}
      </div>
      {subtitle && <p className="px-4 pb-3 text-[length:var(--fs-sub)] text-t2">{subtitle}</p>}
    </header>
  );
}

interface PushedHeaderProps {
  readonly title: string;
  readonly subtitle?: string;
  /** Goes back to the screen this one was pushed over; absent on a tablet's detail, which is not pushed. */
  readonly onBack?: () => void;
  /** The gear, which a tablet's detail carries for the whole split view. */
  readonly gear?: boolean;
}

/**
 * The header of a screen pushed over a tab's first one, or of a tablet's detail beside the list. On an iPhone a round
 * back button and the title in the middle; on Android a back arrow and the title beside it.
 */
export function PushedHeader({ title, subtitle, onBack, gear }: PushedHeaderProps) {
  const { ios } = usePhone();
  const top = useHeaderTop();
  if (ios) {
    return (
      <header className={`flex min-h-[50px] items-center gap-[10px] pb-2 pl-4 pr-3 ${top}`}>
        {onBack && <span className="-ml-1"><IconButton label="Back" onClick={onBack}><ChevronBackIcon /></IconButton></span>}
        <div className={`min-w-0 flex-1 ${onBack ? 'text-center' : ''}`}>
          <h1 className={`truncate font-title ${onBack ? 'text-[17px] font-semibold' : 'text-[22px] font-bold'}`}>{title}</h1>
          {subtitle && <p className="truncate text-[13px] text-t2">{subtitle}</p>}
        </div>
        {/* Keeps the title in the middle, across from the back button. */}
        {onBack && !gear && <span aria-hidden className="w-[var(--ib)] shrink-0" />}
        {gear && <GearButton />}
      </header>
    );
  }
  return (
    <header className="flex h-16 items-center gap-1 pl-4 pr-1">
      {onBack && <span className="-ml-3"><IconButton label="Back" onClick={onBack}><ArrowBackIcon /></IconButton></span>}
      <h1 className="min-w-0 flex-1 truncate font-title text-[22px] font-normal">{title}</h1>
      {gear && <GearButton />}
    </header>
  );
}

interface ScreenProps {
  readonly children: React.ReactNode;
  readonly label: string;
  /** What is inside scrolls itself, under a header that stays. */
  readonly fixed?: boolean;
}

/** A screen that scrolls, header and all, and leaves room at its foot for the bar there. */
export function Screen({ children, label, fixed }: ScreenProps) {
  const { ios, tablet, foot } = usePhone();
  const room = tablet ? 'pb-10' : foot === 'none' ? '' : ios ? 'pb-[120px]' : 'pb-[104px]';
  return <section aria-label={label} className={`flex min-h-0 min-w-0 flex-1 flex-col ${fixed ? 'overflow-hidden' : 'overflow-y-auto'} ${room}`}>{children}</section>;
}

/** A screen pushed over a tab's first one on a phone: it slides in from the right, and the tab bar goes while it shows. */
export function PushedScreen({ children, label }: { readonly children: React.ReactNode; readonly label: string }) {
  return <section aria-label={label} className="flex min-h-0 min-w-0 flex-1 animate-push-in flex-col overflow-y-auto">{children}</section>;
}

/** A tablet's list beside its detail: the list a fixed width with a line on its right, the detail filling the rest. */
export function SplitView({ list, detail }: { readonly list: React.ReactNode; readonly detail: React.ReactNode }) {
  const { ios } = usePhone();
  return (
    <div className="flex min-h-0 min-w-0 flex-1">
      <div className={`flex min-h-0 shrink-0 flex-col overflow-y-auto border-r border-sep ${ios ? 'w-[360px]' : 'w-[400px]'}`}>{list}</div>
      <div className="flex min-h-0 min-w-0 flex-1 flex-col overflow-y-auto">{detail}</div>
    </div>
  );
}
