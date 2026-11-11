import React, { useCallback, useState } from 'react';
import { useBackLayer } from '../../lib/backStack';
import { useForm, WORDS, type Platform, type TabId } from '../../lib/phone';
import type { ThemeChoice } from '../../lib/theme';
import type { ScanProgressSnapshotDto } from '../../types';
import type { Toast, ToastAction, ToastKind } from '../Toaster';
import { useFirstRun, usePhoneScan } from './FirstRun';
import { LibraryScreen } from './Library';
import { useLibrary } from '../../lib/library';
import { PhoneShell } from './PhoneShell';
import { RootHeader, Screen } from './Screens';
import { SettingsSheet } from './SettingsSheet';

interface Props {
  readonly platform: Platform;
  readonly tab: TabId;
  /** A group is being reviewed on its own screen, pushed over Clean up's list. */
  readonly reviewing: boolean;
  /** Clean up's own screens: scanning, or the groups found and their review. */
  readonly cleanup: React.ReactNode;
  /** Organize's own screens. */
  readonly organize: React.ReactNode;
  /** Clean up's subtitle, by which groups are shown. */
  readonly cleanupSubtitle: string;
  /** Something the person should know above every screen, such as the service not answering. */
  readonly notice?: React.ReactNode;
  readonly counts: Partial<Record<TabId, string>>;
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
  onEndReview(): void;
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
 * library, the one folder it has. Clean up and Organize show their screens as they are made today; Library and Send
 * are new, and come step by step.
 */
export function PhoneApp(props: Props) {
  const { platform, tab, reviewing, cleanup, organize, cleanupSubtitle, notice, counts, toasts, theme, version, errorCount, onTab, onEndReview, removeToast, notify } = props;
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
  const pushed = reviewing && tab === 'cleanup' && form === 'phone';
  useBackLayer(pushed, onEndReview);

  return (
    <PhoneShell platform={platform} tab={tab} foot={pushed ? 'none' : tab === 'library' && selecting ? 'selection' : 'tabs'} counts={{ ...counts, library: libraryCount }} toasts={toasts} onTab={onTab} onSettings={openSettings} removeToast={removeToast}
      notice={notice} cover={cover}
      screens={{
        library: <LibraryScreen library={library} selecting={selecting} onSelecting={setSelecting} notify={notify} />,
        cleanup: <Screen label="Clean up" fixed>{!pushed && <RootHeader title="Clean up" subtitle={cleanupSubtitle} />}{cleanup}</Screen>,
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
