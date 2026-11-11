import React, { useCallback, useState } from 'react';
import { useBackLayer } from '../../lib/backStack';
import { useForm, type Platform, type TabId } from '../../lib/phone';
import type { ThemeChoice } from '../../lib/theme';
import { useCleanUp } from '../../lib/cleanup';
import type { ScanProgressSnapshotDto, ScanStatusDto } from '../../types';
import type { Toast, ToastAction, ToastKind } from '../Toaster';
import { CleanUpScreen } from './CleanUp';
import { useFirstRun, usePhoneScan } from './FirstRun';
import { LibraryScreen } from './Library';
import { useLibrary, type Album, type LibraryFilter } from '../../lib/library';
import { useOrganize } from '../../lib/phoneOrganize';
import { OrganizeScreen } from './Organize';
import { PhoneShell } from './PhoneShell';
import { RootHeader, Screen } from './Screens';
import { SettingsSheet } from './SettingsSheet';

interface Props {
  readonly platform: Platform;
  readonly tab: TabId;
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
 * library, the one folder it has. Send is still to come.
 */
export function PhoneApp(props: Props) {
  const { platform, tab, status, notice, toasts, theme, version, errorCount, removeToast, notify } = props;
  const { starting, scan } = usePhoneScan(props.onStarted, notify);
  const cover = useFirstRun({ scanning: props.scanning, progress: props.progress, starting, scan, onStop: props.onStopScan, notify });
  // The library is read once the first run is through, and again after each scan; Library keeps whether it is choosing.
  const library = useLibrary(!cover);
  const [selecting, setSelecting] = useState(false);
  const [filter, setFilter] = useState<LibraryFilter>('all');
  // Albums or folders named in Organize, made once files are first put in them from Library.
  const [made, setMade] = useState<readonly Album[]>([]);
  const libraryCount = library.listing && library.files.length.toLocaleString();
  const [settingsOpen, setSettingsOpen] = useState(false);
  const openSettings = useCallback(() => setSettingsOpen(true), []);
  // On a phone a review is pushed over the list, and Back, or a swipe from the edge, takes it back; a tablet shows both.
  const form = useForm();
  const cleanUp = useCleanUp(form === 'tablet');
  const reviewing = !!cleanUp.group && tab === 'cleanup' && form === 'phone';
  const endReview = () => cleanUp.review(undefined);
  useBackLayer(reviewing, endReview);
  // So is a suggestion's preview over Organize's list.
  const organize = useOrganize(library.listing, platform, form === 'tablet');
  const previewing = !!organize.open && tab === 'organize' && form === 'phone';
  const endPreview = () => organize.look(undefined);
  useBackLayer(previewing, endPreview);
  const pushed = reviewing || previewing;
  // Tapping the tab showing goes back to its first screen.
  const onTab = (next: TabId) => {
    if (next === tab && next === 'cleanup') endReview();
    if (next === tab && next === 'organize') endPreview();
    props.onTab(next);
  };
  const removable = cleanUp.data?.removableCount ?? 0;

  return (
    <PhoneShell platform={platform} tab={tab} foot={pushed ? 'none' : tab === 'library' && selecting ? 'selection' : 'tabs'} counts={{ library: libraryCount, cleanup: removable > 0 ? removable.toLocaleString() : undefined, organize: organize.toMove > 0 ? organize.toMove.toLocaleString() : undefined }} toasts={toasts} onTab={onTab} onSettings={openSettings} removeToast={removeToast}
      notice={notice} cover={cover}
      screens={{
        library: <LibraryScreen library={library} selecting={selecting} onSelecting={setSelecting} filter={filter} onFilter={setFilter} made={made} notify={notify} />,
        cleanup: <CleanUpScreen cleanUp={cleanUp} root={library.root} status={status} notify={notify} onScanAgain={() => void scan()} />,
        organize: <OrganizeScreen organize={organize} library={library} made={made} onMade={album => setMade(m => [...m, album])} notify={notify}
          onNoLocation={() => { setFilter('unplaced'); props.onTab('library'); }} />,
        send: <Screen label="Send to computer"><RootHeader title="Send to computer" subtitle="Copy or move to PhotoSense on a Windows PC or Mac" /></Screen>,
      }}
      sheet={settingsOpen && (
        <SettingsSheet theme={theme} version={version} errorCount={errorCount} onTheme={props.onTheme} onScanAgain={() => void scan()} onDeletePermanently={props.onDeletePermanently}
          onErrors={props.onErrors} onHelp={props.onHelp} onAbout={props.onAbout} onClose={() => setSettingsOpen(false)} />
      )} />
  );
}
