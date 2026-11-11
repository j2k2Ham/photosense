import React from 'react';
import { deviceName, usePhone } from '../../lib/phone';
import type { ThemeChoice } from '../../lib/theme';
import { ListGroup, ListRow, Segmented } from './Controls';
import { Sheet } from './Sheet';

const THEMES = [{ id: 'auto', label: 'Auto' }, { id: 'dark', label: 'Dark' }, { id: 'light', label: 'Light' }] as const;

interface Props {
  readonly theme: ThemeChoice;
  /** The program's version, once known. */
  readonly version?: string;
  readonly errorCount: number;
  onTheme(theme: ThemeChoice): void;
  onScanAgain(): void;
  onDeletePermanently(): void;
  onErrors(): void;
  onHelp(): void;
  onAbout(): void;
  onClose(): void;
}

/** Settings, from the gear: the theme, scanning again, Delete permanently, the errors, and help and about. */
export function SettingsSheet({ theme, version, errorCount, onTheme, onScanAgain, onDeletePermanently, onErrors, onHelp, onAbout, onClose }: Props) {
  const { platform, tablet } = usePhone();
  const device = deviceName(platform, tablet);
  // What a row leads to opens over the screen once the sheet has gone.
  const then = (go: () => void) => () => { onClose(); go(); };
  return (
    <Sheet title="Settings" onClose={onClose}>
      <div className="flex flex-col gap-5 px-4 pb-3 pt-2">
        <ListGroup header="Appearance" inSheet>
          <ListRow detail={<Segmented<ThemeChoice> label="Theme" options={THEMES} value={theme} onChange={onTheme} className="w-[220px]" />}>Theme</ListRow>
        </ListGroup>
        <ListGroup header="Photos" inSheet>
          <ListRow onClick={then(onScanAgain)}><span className="text-acc">Scan again</span></ListRow>
        </ListGroup>
        <ListGroup header="Removed files" inSheet>
          <ListRow danger onClick={then(onDeletePermanently)}>Delete permanently…</ListRow>
        </ListGroup>
        <ListGroup header="History" inSheet>
          <ListRow detail={errorCount === 0 ? 'None' : errorCount.toLocaleString()} onClick={then(onErrors)}>Errors</ListRow>
        </ListGroup>
        <ListGroup header="About" inSheet>
          <ListRow chevron onClick={then(onHelp)}>Help</ListRow>
          <ListRow chevron detail={version} onClick={then(onAbout)}>About PhotoSense</ListRow>
        </ListGroup>
        <p className="px-1 text-center text-[13px] text-t2">{version ? `PhotoSense ${version} · ` : ''}Runs on this {device}. Nothing is uploaded.</p>
      </div>
    </Sheet>
  );
}
