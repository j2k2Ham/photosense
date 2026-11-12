import React, { useCallback, useEffect, useRef, useState } from 'react';
import { appIconUrl } from '../../lib/apiBase';
import { askForPhotos, libraryRoot, openPhotoSettings, startScan, thumbnailUrl, usePhotoAccess } from '../../lib/apiClient';
import { timeLeft } from '../../lib/format';
import { saveLastScan } from '../../lib/lastScan';
import { deviceName, usePhone } from '../../lib/phone';
import type { ScanProgressSnapshotDto } from '../../types';
import type { ToastKind } from '../Toaster';
import { PhoneButton } from './Controls';
import { CleanUpIcon, FolderIcon, ImageIcon, SendIcon } from './Icons';

type Notify = (message: string, kind: ToastKind) => void;
/** How far this phone has got through its first run: not yet welcomed, refused its photos, or through. */
type Stage = 'new' | 'refused' | 'done';

const STAGE_KEY = 'photosense-first-run';
const said = (e: unknown) => (e instanceof Error ? e.message : String(e));

function loadStage(): Stage {
  try {
    const stage = localStorage.getItem(STAGE_KEY);
    return stage === 'refused' || stage === 'done' ? stage : 'new';
  } catch { return 'new'; }
}

function saveStage(stage: Stage) {
  // A phone that cannot remember is welcomed again next time, which loses nothing.
  try { localStorage.setItem(STAGE_KEY, stage); } catch { /* not remembered */ }
}

/** Scanning a phone: the whole of its library, the one folder it has; with whether a scan is being started. */
export function usePhoneScan(onStarted: (instanceId: string) => void, notify: Notify) {
  const [starting, setStarting] = useState(false);
  const scan = useCallback(async () => {
    setStarting(true);
    try {
      const root = await libraryRoot();
      const { instanceId } = await startScan({ primaryLocation: root, recursive: true });
      saveLastScan({ root, second: '', recursive: true });
      onStarted(instanceId);
    } catch (e) {
      notify(`The scan could not start: ${said(e)}`, 'error');
    } finally {
      setStarting(false);
    }
  }, [onStarted, notify]);
  return { starting, scan };
}

interface FirstRunProps {
  /** A scan is under way. */
  readonly scanning: boolean;
  readonly progress?: ScanProgressSnapshotDto;
  /** A scan is being started. */
  readonly starting: boolean;
  /** Starts a scan of the whole library. */
  scan(): Promise<void>;
  /** Stops the scan under way. */
  onStop(): void;
  notify: Notify;
}

/**
 * What covers the tabs until they can be used, or nothing: Welcome the first time, and then the phone's own question
 * of what PhotoSense may see; the screen saying it cannot see them, when it may not; and Scanning while a scan runs.
 * Turning access on in Settings and coming back starts the first scan.
 */
export function useFirstRun({ scanning, progress, starting, scan, onStop, notify }: FirstRunProps): React.ReactElement | null {
  const access = usePhotoAccess().data?.access;
  const [stage, setStage] = useState(loadStage);
  const [asking, setAsking] = useState(false);
  const allowed = access !== undefined && access !== 'nothing';
  // Refused before and allowed now: the person turned access on in Settings and came back, as iOS restarts an app for.
  const cameBack = stage === 'refused' && allowed;
  const begun = useRef(false);

  useEffect(() => {
    if (!cameBack || begun.current) return;
    begun.current = true;
    saveStage('done');
    void scan().then(() => setStage('done'));
  }, [cameBack, scan]);

  async function ask(again: boolean) {
    setAsking(true);
    try {
      const { access: now } = await askForPhotos();
      if (now === 'nothing') {
        if (again) notify('PhotoSense still can\'t see your photos. Turn on access in Settings.', 'info');
        saveStage('refused');
        setStage('refused');
      } else if (stage !== 'refused') {
        saveStage('done');
        setStage('done');
        await scan();
      }
      // Refused before and allowed now, the first scan starts as it does on coming back from Settings.
    } catch (e) {
      notify(`PhotoSense could not ask for your photos: ${said(e)}`, 'error');
    } finally {
      setAsking(false);
    }
  }

  async function settings() {
    try { await openPhotoSettings(); } catch (e) { notify(`Settings could not be opened: ${said(e)}`, 'error'); }
  }

  if (scanning || starting || cameBack) return <Scanning progress={scanning ? progress : undefined} onStop={scanning ? onStop : undefined} />;
  if (stage === 'new') return <Welcome busy={asking} onContinue={() => void ask(false)} />;
  // Until the phone has said what PhotoSense may see, nothing is shown that might be wrong.
  if (access === undefined) return <div aria-busy="true" className="fixed inset-0 z-40 bg-bg" />;
  if (access === 'nothing') return <Refused busy={asking} onAskAgain={() => void ask(true)} onSettings={() => void settings()} />;
  return null;
}

/** A screen of its own over the tabs, its content in a column no wider than 440 pixels, clear of the phone's bars. */
function Cover({ label, children }: { readonly label: string; readonly children: React.ReactNode }) {
  return (
    <section aria-label={label}
      className="fixed inset-0 z-40 flex animate-fade-in justify-center overflow-y-auto bg-bg px-6 pb-[calc(var(--safe-b)+20px)] pt-[calc(var(--safe-t)+28px)] font-ui text-t1">
      <div className="flex w-full max-w-[440px] flex-col">{children}</div>
    </section>
  );
}

/** One thing PhotoSense does, as Welcome lists it: its icon in a tile, a title and a sentence. */
function Point({ Icon, title, children }: { readonly Icon: typeof FolderIcon; readonly title: string; readonly children: React.ReactNode }) {
  return (
    <li className="flex items-start gap-4">
      <span className="grid h-10 w-10 flex-none place-items-center rounded-[12px] bg-acc-soft text-acc"><Icon size={22} /></span>
      <span>
        <span className="block text-[16.5px] font-semibold">{title}</span>
        <span className="block text-[14.5px] text-t2 [text-wrap:pretty]">{children}</span>
      </span>
    </li>
  );
}

/** The first screen: what PhotoSense does, and Continue, which asks for the photos. */
function Welcome({ busy, onContinue }: { readonly busy: boolean; onContinue(): void }) {
  const { platform, tablet, words } = usePhone();
  return (
    <Cover label="Welcome">
      <div className="flex flex-1 flex-col justify-center gap-8">
        <div className="flex flex-col items-center gap-4 text-center">
          <img src={appIconUrl} alt="" className="h-[88px] w-[88px] rounded-[22px]" />
          <h1 className="font-title text-[30px] font-bold tracking-[-0.01em]">Welcome to PhotoSense</h1>
        </div>
        <ul className="flex flex-col gap-[22px]">
          <Point Icon={CleanUpIcon} title="Find copies">The same photo saved twice is kept once. You see what goes and what stays before anything is deleted.</Point>
          <Point Icon={FolderIcon} title="Sort by place and date">Suggested {words.albums} for each place and year, with a preview before anything moves.</Point>
          <Point Icon={SendIcon} title="Send to your computer">Over Wi-Fi to PhotoSense on a PC or Mac. Each file is checked before anything is deleted here.</Point>
        </ul>
      </div>
      <div className="flex flex-col gap-3 pt-6">
        <p className="text-center text-[13px] text-t2">Runs on this {deviceName(platform, tablet)}. Nothing is uploaded.</p>
        <PhoneButton disabled={busy} onClick={onContinue}>Continue</PhoneButton>
      </div>
    </Cover>
  );
}

/** What shows when PhotoSense may see none of the photos: where to turn access on, and asking again. */
function Refused({ busy, onAskAgain, onSettings }: { readonly busy: boolean; onAskAgain(): void; onSettings(): void }) {
  const { ios } = usePhone();
  return (
    <Cover label="Photo access">
      <div className="flex flex-1 flex-col items-center justify-center gap-4 text-center">
        <span className="grid h-[72px] w-[72px] place-items-center rounded-[22px] bg-fill text-t2"><ImageIcon /></span>
        <h1 className="font-title text-[26px] font-bold">{'PhotoSense can\'t see your photos'}</h1>
        <p className="max-w-[340px] text-[15px] text-t2 [text-wrap:pretty]">
          PhotoSense needs your photos to find copies and sort them. Turn on access in Settings › Apps › PhotoSense › {ios ? 'Photos' : 'Permissions'}.
        </p>
      </div>
      <div className="flex flex-col gap-2.5 pt-6">
        <PhoneButton onClick={onSettings}>Open Settings</PhoneButton>
        <PhoneButton kind="secondary" disabled={busy} onClick={onAskAgain}>Ask again</PhoneButton>
      </div>
    </Cover>
  );
}

/** The pictures to show in the tiles, in the order they were read, gathered from each answer's latest few. */
function usePictures(recent: string[] | undefined, count: number): string[] {
  const [kept, setKept] = useState<string[]>([]);
  const [heard, setHeard] = useState(recent);
  if (recent !== heard) {
    setHeard(recent);
    setKept([...kept, ...(recent ?? []).filter(id => !kept.includes(id))].slice(0, count));
  }
  return kept;
}

/** While a scan runs: how far it has got, how long it has left, and tiles that fill with the pictures as they are read. */
function Scanning({ progress, onStop }: { readonly progress?: ScanProgressSnapshotDto; onStop?(): void }) {
  const { tablet } = usePhone();
  const count = tablet ? 96 : 80;
  const pictures = usePictures(progress?.recent, count);
  // Until the files have been counted there is nothing to be a percentage of.
  const total = progress ? progress.primaryTotal + progress.secondaryTotal : 0;
  const done = progress ? progress.primaryProcessed + progress.secondaryProcessed : 0;
  const percent = progress && total > 0 ? Math.floor(progress.overallPercent) : 0;
  const lit = Math.floor((percent / 100) * count);
  return (
    <Cover label="Scanning">
      <div className="flex flex-1 flex-col justify-center gap-5">
        <h1 className="font-title text-[26px] font-bold">Reading your photos</h1>
        <p className="flex items-baseline gap-[14px]">
          <span className="font-title text-[56px] font-bold leading-none tracking-[-0.02em] tabular-nums">{percent}%</span>
          <span className="text-[14px] text-t2">{timeLeft(progress?.secondsLeft)}</span>
        </p>
        <div role="progressbar" aria-label="Photos read" aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent} className="h-2 overflow-hidden rounded-full bg-fill">
          <div className="h-full rounded-full bg-acc transition-[width] duration-500" style={{ width: `${percent}%` }} />
        </div>
        <p className="text-[15px] tabular-nums">{total > 0 ? `Read ${done.toLocaleString()} of ${total.toLocaleString()} photos and videos` : 'Counting your photos and videos…'}</p>
        <div aria-hidden className={`grid gap-1 ${tablet ? 'grid-cols-[repeat(16,minmax(0,1fr))]' : 'grid-cols-[repeat(10,minmax(0,1fr))]'}`}>
          {Array.from({ length: count }, (_, i) => <Tile key={i} lit={i < lit} picture={pictures[i]} shade={i} />)}
        </div>
        <p className="text-[13.5px] text-t2 [text-wrap:pretty]">Keep PhotoSense open until it finishes. Later scans skip photos already read, so they take seconds.</p>
      </div>
      <div className="pt-6">
        <PhoneButton kind="secondary" disabled={!onStop} onClick={() => onStop?.()}>Stop</PhoneButton>
      </div>
    </Cover>
  );
}

/** One tile of the scan: empty until the scan gets that far, then a picture it read, or the accent where it has none to show. */
function Tile({ lit, picture, shade }: { readonly lit: boolean; readonly picture?: string; readonly shade: number }) {
  if (!lit) return <span className="aspect-square rounded-[4px] bg-fill" />;
  if (!picture) return <span className="aspect-square rounded-[4px] bg-acc" style={{ opacity: 0.35 + ((shade * 7) % 10) / 20 }} />;
  return <img src={thumbnailUrl(picture)} alt="" className="aspect-square w-full rounded-[4px] bg-fill object-cover" />;
}
