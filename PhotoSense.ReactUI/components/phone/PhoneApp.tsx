import React, { useCallback, useEffect, useState } from 'react';
import { useBackLayer } from '../../lib/backStack';
import { useForm, WORDS, type Platform, type TabId } from '../../lib/phone';
import { backUpIfDue, eraseRemoved, openPhotoSettings, startBackup, useBackup, useLibraryBackup, useOrganizeBatches, usePhotoAccess, useRemoved, useSafeReview, useSwipe } from '../../lib/apiClient';
import type { AppSettings } from '../../lib/settings';
import type { AboutTopic } from '../AboutWindow';
import type { ThemeChoice } from '../../lib/theme';
import { useCleanUp } from '../../lib/cleanup';
import type { SafeRemovalDto, ScanProgressSnapshotDto, ScanStatusDto } from '../../types';
import type { Toast, ToastAction, ToastKind } from '../Toaster';
import { CleanUpScreen } from './CleanUp';
import { Pair, useFirstRun, usePhoneScan } from './FirstRun';
import { BackupScreen, SafeToDelete, SpaceFreed } from './Backup';
import { LibraryScreen } from './Library';
import { items as itemsSaid, useLibrary, type Album, type LibraryFilter } from '../../lib/library';
import { useOrganize } from '../../lib/phoneOrganize';
import { OrganizeScreen, RecentSheet, undoMove } from './Organize';
import { PhoneShell } from './PhoneShell';
import { DeletePermanently, items, SettingsSheet } from './SettingsSheet';
import { PlanProvider, usePlanSheet } from './ProSheet';
import { SubscriptionScreen } from './Subscription';
import { SwipeSession, SwipeSummary } from './Swipe';
import { marked } from '../../lib/swipe';

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
  /** How the person has set PhotoSense to behave: the unit of distance, whether Delete permanently asks twice, and the trial's reminder. */
  readonly settings: AppSettings;
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
  onSettings(change: Partial<AppSettings>): void;
  onErrors(): void;
  onHelp(): void;
  /** Opens About, at PhotoSense itself or at one of its documents. */
  onAbout(topic?: AboutTopic): void;
}

const said = (e: unknown) => (e instanceof Error ? e.message : String(e));

/**
 * PhotoSense on a phone or tablet: the four tabs in the shell, each with its header and the gear that opens Settings,
 * with Welcome, the photo access screen and Scanning over them when they are called for. A phone scans its whole
 * library, the one folder it has, and backs up to the computer it was paired with, by itself when one is due as the app
 * opens.
 */
export function PhoneApp(props: Props) {
  const { platform, tab, status, notice, toasts, theme, version, errorCount, removeToast, notify } = props;
  const { starting, scan } = usePhoneScan(props.onStarted, notify);
  const cover = useFirstRun({ scanning: props.scanning, progress: props.progress, starting, scan, onStop: props.onStopScan, onCleanUp: () => props.onTab('cleanup'), notify });
  // The library is read once the first run is through, and again after each scan; Library keeps whether it is choosing.
  const library = useLibrary(!cover);
  const [selecting, setSelecting] = useState(false);
  const [filter, setFilter] = useState<LibraryFilter>('all');
  // Albums or folders named in Organize, made once files are first put in them from Library.
  const [made, setMade] = useState<readonly Album[]>([]);
  const libraryCount = library.listing && library.files.length.toLocaleString();
  // What is over everything: Settings, or what a row of it leads to.
  const [sheet, setSheet] = useState<'settings' | 'subscription' | 'erase' | 'recent' | 'safe' | 'pair'>();
  // What Safe to delete last deleted, for Space freed.
  const [freed, setFreed] = useState<{ removed: SafeRemovalDto; partial: boolean }>();
  const openSettings = useCallback(() => setSheet('settings'), []);
  const closeSheet = () => setSheet(undefined);
  // On a phone a review is pushed over the list, and Back, or a swipe from the edge, takes it back; a tablet shows both.
  const form = useForm();
  const cleanUp = useCleanUp(form === 'tablet');
  const reviewing = !!cleanUp.group && tab === 'cleanup' && form === 'phone';
  const endReview = () => cleanUp.review(undefined);
  // A list opened from Find is pushed over Clean up's first screen, and goes back to it.
  const listing = cleanUp.listing && tab === 'cleanup';
  useBackLayer(listing, cleanUp.closeList);
  useBackLayer(reviewing, endReview);
  // So is a suggestion's preview over Organize's list.
  const organize = useOrganize(library.listing, platform, form === 'tablet', props.settings.distanceUnit);
  const previewing = !!organize.open && tab === 'organize' && form === 'phone';
  const endPreview = () => organize.look(undefined);
  useBackLayer(previewing, endPreview);
  const pushed = (listing && form === 'phone') || reviewing || previewing;
  // Backing up: asked while the app shows its tabs; a backup due runs by itself as they first show.
  const ready = !cover;
  const backup = useBackup(ready).data;
  const paired = !!backup?.computer;
  const libraryBackup = useLibraryBackup(ready && paired).data;
  const safe = useSafeReview((ready && paired) || sheet === 'safe').data;
  useEffect(() => { if (ready) void backUpIfDue().catch(() => undefined); }, [ready]);
  // Swipe to clean: today's set, asked once the tabs show; swiping and its summary cover everything while they show.
  const swipe = useSwipe(ready).data;
  const [swiping, setSwiping] = useState<'cards' | 'summary'>();
  const stopSwiping = () => setSwiping(swipe && marked(swipe).length > 0 ? 'summary' : undefined);
  useBackLayer(sheet === 'pair', () => setSheet(undefined));
  async function send(ids: string[]) {
    try {
      await startBackup(ids);
      props.onTab('backup');
      notify(`Sending ${itemsSaid(ids.length)} to ${backup?.computer}.`, 'ok');
    } catch (e) {
      notify(said(e), 'error');
    }
  }
  // Tapping the tab showing goes back to its first screen.
  const onTab = (next: TabId) => {
    if (next === tab && next === 'cleanup') { endReview(); cleanUp.closeList(); }
    if (next === tab && next === 'organize') endPreview();
    props.onTab(next);
  };
  const removable = cleanUp.data?.removableCount ?? 0;
  // What Settings says of the photos, of what waits to be erased (in the library, once it is known, and wherever else the
  // service knows of), and of what Undo can still put back.
  const access = usePhotoAccess().data?.access;
  const removed = useRemoved(sheet === 'settings' || sheet === 'erase' ? [library.root].filter(root => root !== undefined) : null).data;
  const batches = useOrganizeBatches().data;
  const [busy, setBusy] = useState(false);
  const bin = WORDS[platform].bin;
  // The plan: every removal asks it first, and the Pro sheet opens when the free plan has too few left today.
  const { gate, sheet: proSheet } = usePlanSheet(notify);

  async function erase(askOnce: boolean) {
    setBusy(true);
    try {
      if (askOnce) props.onSettings({ eraseAskTwice: false });
      const result = await eraseRemoved(removed!.folders.map(f => f.path));
      if (result.erased > 0) notify(`${items(result.erased)} deleted. They wait in ${bin} for 30 days.`, 'ok');
      if (result.skipped > 0) notify(`${items(result.skipped)} could not be deleted. ${result.problems[0] ?? ''}`.trim(), 'error');
      if (result.erased + result.skipped === 0) notify('There was nothing left to delete.', 'info');
    } catch (e) {
      notify(said(e), 'error');
    } finally {
      setBusy(false);
      setSheet(undefined);
    }
  }
  async function undo(batchId: string) {
    setBusy(true);
    await undoMove(batchId, organize, notify);
    setBusy(false);
  }

  return (
    <PlanProvider value={gate}>
    <PhoneShell platform={platform} tab={tab} foot={pushed ? 'none' : tab === 'library' && selecting ? 'selection' : 'tabs'} counts={{ library: libraryCount, cleanup: removable > 0 ? removable.toLocaleString() : undefined, organize: organize.toMove > 0 ? organize.toMove.toLocaleString() : undefined }} toasts={toasts} onTab={onTab} onSettings={openSettings} removeToast={removeToast}
      notice={notice} cover={cover}
      screens={{
        library: <LibraryScreen library={library} selecting={selecting} onSelecting={setSelecting} filter={filter} onFilter={setFilter} made={made} notify={notify}
          backup={libraryBackup} safe={safe} onReview={() => setSheet('safe')} onSend={ids => void send(ids)} />,
        cleanup: <CleanUpScreen cleanUp={cleanUp} root={library.root} status={status} notify={notify} onScanAgain={() => void scan()} swipe={swipe} library={library} onSwipe={setSwiping} />,
        organize: <OrganizeScreen organize={organize} library={library} made={made} onMade={album => setMade(m => [...m, album])} notify={notify}
          onNoLocation={() => { setFilter('unplaced'); props.onTab('library'); }} />,
        backup: <BackupScreen state={backup} notify={notify} onChoose={() => { setSelecting(true); props.onTab('library'); }} onPair={() => setSheet('pair')} />,
      }}
      sheet={<>
        {sheet === 'settings' && (
          <SettingsSheet theme={theme} unit={props.settings.distanceUnit} version={version} errorCount={errorCount} access={access} removed={removed} moves={batches?.length}
            plan={gate.plan} onSubscription={() => setSheet('subscription')}
            computer={backup && (backup.computer ? { name: backup.computer, folder: backup.folder } : null)} onPair={() => setSheet('pair')}
            onTheme={props.onTheme} onUnit={distanceUnit => props.onSettings({ distanceUnit })} onPhotoAccess={() => void openPhotoSettings().catch(e => notify(said(e), 'error'))}
            onScanAgain={() => void scan()} onDeletePermanently={() => setSheet('erase')} onRecent={() => setSheet('recent')} onErrors={props.onErrors} onHelp={props.onHelp}
            onAbout={() => props.onAbout()} onPrivacy={() => props.onAbout('privacy')} onClose={closeSheet} />
        )}
        {sheet === 'subscription' && gate.plan && (
          <SubscriptionScreen plan={gate.plan} remind={props.settings.remindTrialEnd} onRemind={remindTrialEnd => props.onSettings({ remindTrialEnd })} notify={notify}
            onTerms={() => props.onAbout('terms')} onPrivacy={() => props.onAbout('privacy')} onBack={() => setSheet('settings')} />
        )}
        {sheet === 'erase' && removed && <DeletePermanently removed={removed} askTwice={props.settings.eraseAskTwice} busy={busy} onErase={askOnce => void erase(askOnce)} onCancel={closeSheet} />}
        {sheet === 'recent' && <RecentSheet batches={batches ?? []} root={library.root} busy={busy} onUndo={b => void undo(b.id)} onClose={closeSheet} />}
        {sheet === 'safe' && (
          <SafeToDelete safe={safe} notify={notify} onBack={closeSheet} onRemoved={(removed, partial) => { setSheet(undefined); setFreed({ removed, partial }); }} />
        )}
        {sheet === 'pair' && <Pair later notify={notify} onNext={closeSheet} />}
        {freed && (
          <SpaceFreed removed={freed.removed} partial={freed.partial} computer={backup?.computer ?? 'the computer'} folder={backup?.folder ?? ''}
            onPro={() => gate.offer('none')} onDone={() => { setFreed(undefined); props.onTab('library'); }} />
        )}
        {swiping === 'cards' && swipe && (
          <SwipeSession state={swipe} library={library} computer={backup?.computer} notify={notify} onClose={stopSwiping} onDone={() => setSwiping('summary')} />
        )}
        {swiping === 'summary' && swipe && <SwipeSummary state={swipe} library={library} notify={notify} onClose={() => setSwiping(undefined)} />}
        {proSheet}
      </>} />
    </PlanProvider>
  );
}
