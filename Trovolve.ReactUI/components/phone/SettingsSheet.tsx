import React from 'react';
import { appIconUrl } from '../../lib/apiClient';
import { DISTANCE_UNITS, type DistanceUnit } from '../../lib/distance';
import { formatBytes } from '../../lib/format';
import { planLine, planTitle } from '../../lib/plan';
import { usePhone } from '../../lib/phone';
import type { ThemeChoice } from '../../lib/theme';
import type { PhotoAccessDto, PlanDto, RemovedFilesDto } from '../../types';
import { ListGroup, ListRow, PhoneSwitch, Segmented } from './Controls';
import { ChevronIcon } from './Icons';
import { LayerScreen, PushedHeader, RootHeader, SplitView, useInPlace } from './Screens';
import { ConfirmSheet } from './Sheet';

const UNITS = DISTANCE_UNITS.map(u => ({ id: u.value, label: u.name }));
const THEMES = [{ id: 'auto', label: 'Auto' }, { id: 'dark', label: 'Dark' }, { id: 'light', label: 'Light' }] as const;

/** "1 item", "37 items". */
export const items = (n: number) => `${n.toLocaleString()} ${n === 1 ? 'item' : 'items'}`;

interface Props {
  readonly theme: ThemeChoice;
  /** The unit distances are said in. */
  readonly unit: DistanceUnit;
  /** The program's version, once known. */
  readonly version?: string;
  readonly errorCount: number;
  /** How much of the photos Trovolve may see, once the phone has said. */
  readonly access?: PhotoAccessDto['access'];
  /** What waits in Trovolve Removed from before deleting went to the phone's bin, once the service has said. */
  readonly removed?: RemovedFilesDto;
  /** How many moves and deletions Undo can still put back, once known. */
  readonly moves?: number;
  /** The plan, once the service has said. */
  readonly plan?: PlanDto;
  /** Whether low-storage alerts are on, once the phone has said. */
  readonly lowStorage?: boolean;
  /** Turns low-storage alerts on or off; on asks the phone first. */
  onLowStorage(on: boolean): void;
  /** Whether Share photo starts with Remove location on. */
  readonly removeLocation?: boolean;
  /** Changes whether Share photo starts with Remove location on; without it, Privacy is not shown. */
  onRemoveLocation?(on: boolean): void;
  /** Opens the Subscription page. */
  onSubscription(): void;
  onTheme(theme: ThemeChoice): void;
  onUnit(unit: DistanceUnit): void;
  /** Opens the phone's own settings for Trovolve, where its access to the photos is changed. */
  onPhotoAccess(): void;
  onScanAgain(): void;
  /** Offers to move what waits in Trovolve Removed to Recently Deleted or the bin. */
  onMoveRemoved(): void;
  onRecent(): void;
  onErrors(): void;
  onHelp(): void;
  onAbout(): void;
  onPrivacy(): void;
  onClose(): void;
  /** On a tablet, what shows beside the list: the Subscription page. */
  readonly beside?: React.ReactNode;
  /** The places backups go, the computer among them, with their switches. */
  readonly locations?: React.ReactNode;
}

/**
 * Settings, from the gear, over every tab: the plan, which opens Subscription; the locations backups go to, the
 * computer among them; the theme and the unit of distance; the
 * photos Trovolve may see and scanning again; while anything waits in Trovolve Removed from before, what does and
 * moving it to the phone's bin; what was moved and what went wrong; sharing without the place; and help, about and privacy.
 */
export function SettingsSheet(p: Props) {
  const { ios, words } = usePhone();
  const inPlace = useInPlace();
  // What a row leads to opens over the screen once Settings has gone; on a tablet Settings stays beside it.
  const then = (go: () => void) => () => { if (!inPlace) p.onClose(); go(); };
  const access = p.access === undefined ? undefined
    : p.access === 'everything' ? (ios ? 'Full access' : 'All photos and videos') : p.access === 'selected' ? 'Some chosen' : 'None';
  const rows = (
      <div className="flex flex-col gap-5 px-4 pb-6 pt-2">
        {p.plan && <PlanSummary plan={p.plan} on={inPlace} onOpen={p.onSubscription} />}
        {p.locations}
        <ListGroup header="Appearance">
          <ListRow detail={<Segmented<ThemeChoice> label="Theme" options={THEMES} value={p.theme} onChange={p.onTheme} className="w-[220px]" />}>Theme</ListRow>
          <ListRow detail={<Segmented<DistanceUnit> label="Distances" options={UNITS} value={p.unit} onChange={p.onUnit} className="w-[220px]" />}>Distances</ListRow>
        </ListGroup>
        {p.lowStorage !== undefined && (
          <ListGroup header="Clean up">
            <PhoneSwitch inList label="Low-storage alerts" detail="When less than 10% is free, at most once a week" on={p.lowStorage} onChange={p.onLowStorage} />
          </ListGroup>
        )}
        <ListGroup header="Photos">
          <ListRow chevron detail={access} onClick={then(p.onPhotoAccess)}>Photo access</ListRow>
          <ListRow onClick={then(p.onScanAgain)}><span className="text-acc">Scan again</span></ListRow>
        </ListGroup>
        {/* Nothing new goes there (Jamie, 2026-11-20): shown only until what waits from before has gone to the bin. */}
        {p.removed !== undefined && p.removed.files > 0 && (
          <ListGroup header="Removed files">
            <ListRow detail={`${items(p.removed.files)} · ${formatBytes(p.removed.bytes)}`}>{words.removed}</ListRow>
            <ListRow danger onClick={then(p.onMoveRemoved)}>Move to {words.bin}…</ListRow>
          </ListGroup>
        )}
        <ListGroup header="History">
          <ListRow chevron detail={p.moves === undefined ? undefined : p.moves.toLocaleString()} onClick={then(p.onRecent)}>Recently moved</ListRow>
          <ListRow detail={p.errorCount === 0 ? 'None' : p.errorCount.toLocaleString()} onClick={then(p.onErrors)}>Errors</ListRow>
        </ListGroup>
        {p.onRemoveLocation && (
          <ListGroup header="Privacy">
            <PhoneSwitch inList label="Remove location when sharing" detail="Applies to copies you share from Trovolve" on={p.removeLocation ?? true} onChange={p.onRemoveLocation} />
          </ListGroup>
        )}
        <ListGroup header="About">
          <ListRow chevron onClick={then(p.onHelp)}>Help</ListRow>
          <ListRow chevron detail={p.version} onClick={then(p.onAbout)}>About Trovolve</ListRow>
          <ListRow chevron onClick={then(p.onPrivacy)}>Privacy policy</ListRow>
        </ListGroup>
        <p className="px-4 text-center text-[13px] leading-[1.45] text-t2">Your photos stay on your own devices. Trovolve has no ads and does not track what is in your photos.</p>
      </div>
  );
  // T5: on a tablet, beside the sidebar or rail, the list with the Subscription page beside it.
  if (inPlace) {
    return (
      <section aria-label="Settings" className="flex min-h-0 min-w-0 flex-1">
        <SplitView narrow list={<><RootHeader title="Settings" noGear />{rows}</>} detail={p.beside} />
      </section>
    );
  }
  return (
    <LayerScreen label="Settings" onBack={p.onClose}>
      <PushedHeader title="Settings" onBack={p.onClose} />
      {rows}
    </LayerScreen>
  );
}

/**
 * The plan at the top of Settings: what it is and what it comes to, with Get Pro on the free plan. It opens Subscription,
 * which on a tablet shows beside the list, the plan lit.
 */
function PlanSummary({ plan, on, onOpen }: { readonly plan: PlanDto; readonly on: boolean; onOpen(): void }) {
  const title = planTitle(plan);
  return (
    <button type="button" onClick={onOpen} aria-label={`${title}. Subscription`} aria-current={on ? 'true' : undefined}
      className={`flex items-center gap-3.5 rounded-cell p-4 text-left ${on ? 'bg-sel' : 'bg-cell'}`}>
      <img src={appIconUrl} alt="" className="h-12 w-12 shrink-0 rounded-[12px]" />
      <span className="flex min-w-0 flex-1 flex-col gap-0.5">
        <span className="text-[17px] font-semibold">{title}</span>
        <span className="text-[14px] text-t2">{planLine(plan, new Date())}</span>
      </span>
      {plan.tier === 'free'
        ? <span className="flex h-9 shrink-0 items-center rounded-full bg-acc-soft px-3.5 text-[14px] font-semibold text-on-acc-soft">Get Pro</span>
        : <span className="shrink-0 text-t3"><ChevronIcon /></span>}
    </button>
  );
}

interface MoveProps {
  readonly removed: RemovedFilesDto;
  readonly busy: boolean;
  /** Moves what waits to the phone's bin, after the phone's own question. */
  onMove(): void;
  /** Leaves it where it is, for Settings to offer again. */
  onLater(): void;
}

/**
 * What waits in Trovolve Removed from before deleting on a phone went straight to its bin (Jamie, 2026-11-20): offered
 * once after the update, and from Settings while anything waits, it goes to Recently Deleted or the bin after the phone's
 * own question. It does not count toward the day's free removals: each was counted when it was first removed.
 */
export function MoveRemoved({ removed, busy, onMove, onLater }: MoveProps) {
  const { ios, words } = usePhone();
  const n = items(removed.files), one = removed.files === 1;
  return (
    <ConfirmSheet title={`${n} ${one ? 'waits' : 'wait'} in ${words.removed}`} confirmLabel={`Move ${n}`} cancelLabel="Not now" busy={busy} onCancel={onLater} onConfirm={onMove}
      lines={[
        `Move ${one ? 'it' : 'them'} to ${words.bin} (${formatBytes(removed.bytes)})? ${ios ? 'iOS' : 'Android'} keeps ${one ? 'it' : 'them'} there for 30 days.`,
        `Trovolve now deletes straight to ${words.bin}, so nothing new goes to ${words.removed}.`,
      ]} />
  );
}
