import React, { useCallback, useState } from 'react';
import { useBackLayer } from '../../lib/backStack';
import { useForm, WORDS, type Platform, type TabId } from '../../lib/phone';
import type { ThemeChoice } from '../../lib/theme';
import { useCleanUp } from '../../lib/cleanup';
import type { ScanProgressSnapshotDto, ScanStatusDto } from '../../types';
import type { Toast, ToastAction, ToastKind } from '../Toaster';
import { CleanUpScreen } from './CleanUp';
import { useFirstRun, usePhoneScan } from './FirstRun';
import { LibraryScreen } from './Library';
import { useLibrary } from '../../lib/library';
import { PhoneShell } from './PhoneShell';
import { RootHeader, Screen } from './Screens';
import { SettingsSheet } from './SettingsSheet';

interface Props {
  readonly platform: Platform;
  readonly tab: TabId;
  /** Organize's own screens. */
  readonly organize: React.ReactNode;
  /** What the service holds, and when its last scan finished. */
  readonly status?: ScanStatusDto;
  /** Something the person should know above every screen, such as the service not answering. */
  readonly notice?: React.ReactNode;
  readonly toasts: Toast[];
  readonly theme: ThemeChoice;
  readonly version?: string;
  readonly errorCount: number;
  /** A scan is under way. */
  readonly scanning: boolean;
  readonly progress?: ScanProgressSnapshotDto;
  /** A scan has been started, under the id given. */
  onStarted(instanceId: string): void;
  /** Stops the scan under way. */
  onStopScan(): void;
  notify(message: string, kind: ToastKind, action?: ToastAction): void;
  onTab(tab: TabId): void;
  removeToast(id: number): void;
  onTheme(theme: ThemeChoice): void;
  onDeletePermanently(): void;
  onErrors(): void;
  onHelp(): void;
  onAbout(): void;
}

/**
 * PhotoSense on a phone or tablet: the four tabs in the shell, each with its header and the gear that opens Settings,
 * with Welcome, the photo access screen and Scanning over them when they are called for. A phone scans its whole
 * library, the one folder it has. Organize shows its screens as they are made today, and Send is still to come.
 */
export function PhoneApp(props: Props) {
  const { platform, tab, organize, status, notice, toasts, theme, version, errorCount, removeToast, notify } = props;
  const { starting, scan } = usePhoneScan(props.onStarted, notify);
  const cover = useFirstRun({ scanning: props.scanning, progress: props.progress, starting, scan, onStop: props.onStopScan, notify });
  // The library is read once the first run is through, and again after each scan; Library keeps whether it is choosing.
  const library = useLibrary(!cover);
  const [selecting, setSelecting] = useState(false);
  const libraryCount = library.listing && library.files.length.toLocaleString();
  const [settingsOpen, setSettingsOpen] = useState(false);
  const openSettings = useCallback(() => setSettingsOpen(true), []);
  const words = WORDS[platform];
  // On a phone a review is pushed over the list, and Back, or a swipe from the edge, takes it back; a tablet shows both.
  const form = useForm();
  const cleanUp = useCleanUp(form === 'tablet');
  const pushed = !!cleanUp.group && tab === 'cleanup' && form === 'phone';
  const endReview = () => cleanUp.review(undefined);
  useBackLayer(pushed, endReview);
  // Tapping the tab showing goes back to its first screen.
  const onTab = (next: TabId) => { if (next === tab && next === 'cleanup') endReview(); props.onTab(next); };
  const removable = cleanUp.data?.removableCount ?? 0;

  return (
    <PhoneShell platform={platform} tab={tab} foot={pushed ? 'none' : tab === 'library' && selecting ? 'selection' : 'tabs'} counts={{ library: libraryCount, cleanup: removable > 0 ? removable.toLocaleString() : undefined }} toasts={toasts} onTab={onTab} onSettings={openSettings} removeToast={removeToast}
      notice={notice} cover={cover}
      screens={{
        library: <LibraryScreen library={library} selecting={selecting} onSelecting={setSelecting} notify={notify} />,
        cleanup: <CleanUpScreen cleanUp={cleanUp} root={library.root} status={status} notify={notify} onScanAgain={() => void scan()} />,
        // Until Organize is rebuilt for phones, its screen scrolls as a whole, at least as tall as a phone held upright,
        // so that on a short screen nothing of it stays under the tab bar.
        organize: <Screen label="Organize"><RootHeader title="Organize" subtitle={`Sort ${words.library} into ${words.albums} by place and date`} /><div className="flex h-[max(600px,100%)] shrink-0 flex-col">{organize}</div></Screen>,
        send: <Screen label="Send to computer"><RootHeader title="Send to computer" subtitle="Copy or move to PhotoSense on a Windows PC or Mac" /></Screen>,
      }}
      sheet={settingsOpen && (
        <SettingsSheet theme={theme} version={version} errorCount={errorCount} onTheme={props.onTheme} onScanAgain={() => void scan()} onDeletePermanently={props.onDeletePermanently}
          onErrors={props.onErrors} onHelp={props.onHelp} onAbout={props.onAbout} onClose={() => setSettingsOpen(false)} />
      )} />
  );
}
