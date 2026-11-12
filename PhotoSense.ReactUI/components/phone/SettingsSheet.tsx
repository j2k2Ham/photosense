import React, { useState } from 'react';
import { DISTANCE_UNITS, type DistanceUnit } from '../../lib/distance';
import { formatBytes } from '../../lib/format';
import { deviceName, usePhone } from '../../lib/phone';
import type { ThemeChoice } from '../../lib/theme';
import type { PhotoAccessDto, RemovedFilesDto } from '../../types';
import { ListGroup, ListRow, PhoneSwitch, Segmented } from './Controls';
import { ConfirmSheet, Sheet } from './Sheet';

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
  /** How much of the photos PhotoSense may see, once the phone has said. */
  readonly access?: PhotoAccessDto['access'];
  /** What waits in PhotoSense Removed, once the service has said. */
  readonly removed?: RemovedFilesDto;
  /** How many moves and deletions Undo can still put back, once known. */
  readonly moves?: number;
  onTheme(theme: ThemeChoice): void;
  onUnit(unit: DistanceUnit): void;
  /** Opens the phone's own settings for PhotoSense, where its access to the photos is changed. */
  onPhotoAccess(): void;
  onScanAgain(): void;
  onDeletePermanently(): void;
  onRecent(): void;
  onErrors(): void;
  onHelp(): void;
  onAbout(): void;
  onPrivacy(): void;
  onClose(): void;
}

/**
 * Settings, from the gear: the theme and the unit of distance; the photos PhotoSense may see and scanning again; what
 * waits in PhotoSense Removed and Delete permanently; what was moved and what went wrong; and help, about and privacy.
 */
export function SettingsSheet(p: Props) {
  const { platform, tablet, ios } = usePhone();
  const device = deviceName(platform, tablet);
  // What a row leads to opens over the screen once the sheet has gone.
  const then = (go: () => void) => () => { p.onClose(); go(); };
  const access = p.access === undefined ? undefined
    : p.access === 'everything' ? (ios ? 'Full access' : 'All photos and videos') : p.access === 'selected' ? 'Some chosen' : 'None';
  const empty = p.removed !== undefined && p.removed.files === 0;
  return (
    <Sheet title="Settings" onClose={p.onClose}>
      <div className="flex flex-col gap-5 px-4 pb-3 pt-2">
        <ListGroup header="Appearance" inSheet>
          <ListRow detail={<Segmented<ThemeChoice> label="Theme" options={THEMES} value={p.theme} onChange={p.onTheme} className="w-[220px]" />}>Theme</ListRow>
          <ListRow detail={<Segmented<DistanceUnit> label="Distances" options={UNITS} value={p.unit} onChange={p.onUnit} className="w-[220px]" />}>Distances</ListRow>
        </ListGroup>
        <ListGroup header="Photos" inSheet>
          <ListRow chevron detail={access} onClick={then(p.onPhotoAccess)}>Photo access</ListRow>
          <ListRow onClick={then(p.onScanAgain)}><span className="text-acc">Scan again</span></ListRow>
        </ListGroup>
        <ListGroup header="Removed files" inSheet>
          <ListRow detail={p.removed === undefined ? '…' : empty ? 'Empty' : `${items(p.removed.files)} · ${formatBytes(p.removed.bytes)}`}>PhotoSense Removed</ListRow>
          <ListRow danger={!empty} disabled={p.removed === undefined || empty} onClick={then(p.onDeletePermanently)}>
            <span className={empty ? 'text-t3' : undefined}>Delete permanently…</span>
          </ListRow>
        </ListGroup>
        <ListGroup header="History" inSheet>
          <ListRow chevron detail={p.moves === undefined ? undefined : p.moves.toLocaleString()} onClick={then(p.onRecent)}>Recently moved</ListRow>
          <ListRow detail={p.errorCount === 0 ? 'None' : p.errorCount.toLocaleString()} onClick={then(p.onErrors)}>Errors</ListRow>
        </ListGroup>
        <ListGroup header="About" inSheet>
          <ListRow chevron onClick={then(p.onHelp)}>Help</ListRow>
          <ListRow chevron detail={p.version} onClick={then(p.onAbout)}>About PhotoSense</ListRow>
          <ListRow chevron onClick={then(p.onPrivacy)}>Privacy</ListRow>
        </ListGroup>
        <p className="px-1 text-center text-[13px] text-t2">{p.version ? `PhotoSense ${p.version} · ` : ''}Runs on this {device}. Nothing is uploaded.</p>
      </div>
    </Sheet>
  );
}

interface EraseProps {
  readonly removed: RemovedFilesDto;
  /** Whether to ask a second time, as Settings has it. */
  readonly askTwice: boolean;
  readonly busy: boolean;
  /** Erases what waits, saying whether not to ask the second time again. */
  onErase(askOnce: boolean): void;
  onCancel(): void;
}

/**
 * Delete permanently on a phone: what waits in PhotoSense Removed goes to the phone's own bin, after a question, and a
 * second one unless the person has said not to ask it again. The phone then asks too.
 */
export function DeletePermanently({ removed, askTwice, busy, onErase, onCancel }: EraseProps) {
  const { ios, words } = usePhone();
  const [sure, setSure] = useState(false);
  const [askOnce, setAskOnce] = useState(false);
  const n = items(removed.files);
  if (!sure) {
    return (
      <ConfirmSheet title={`Delete ${n} permanently?`} confirmLabel={`Delete ${n}`} busy={busy} onCancel={onCancel}
        onConfirm={() => (askTwice ? setSure(true) : onErase(false))}
        lines={[
          `Everything in ${words.removed} (${formatBytes(removed.bytes)}) goes to ${words.bin}, where ${ios ? 'iOS' : 'Android'} keeps it for 30 days.`,
          'Undo can no longer bring these back, and they leave Recently moved.',
        ]} />
    );
  }
  return (
    <ConfirmSheet title="Are you sure?" confirmLabel={`Delete ${n}`} busy={busy} onCancel={onCancel} onConfirm={() => onErase(askOnce)}
      lines={[`The ${n} in ${words.removed} ${removed.files === 1 ? 'goes' : 'go'} to ${words.bin}. Undo can no longer bring ${removed.files === 1 ? 'it' : 'them'} back.`]}>
      <div className="pt-2"><PhoneSwitch label="Do not ask me this second time again" on={askOnce} onChange={setAskOnce} /></div>
    </ConfirmSheet>
  );
}
