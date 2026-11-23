import React, { useCallback, useEffect, useState } from 'react';
import { appIconUrl } from '../../lib/apiBase';
import { BACKUP_NOTE, useBackupNote } from '../../lib/backupNote';
import {
  askForPhotos, buyPlan, confirmPairing, enterPairingCode, libraryRoot, openPhotoSettings, scanPairing, startScan, stopPairing, useAbout, useFound, usePairing, usePhotoAccess, usePlan,
} from '../../lib/apiClient';
import { formatBytes } from '../../lib/format';
import { saveLastScan } from '../../lib/lastScan';
import { offeredPlans } from '../../lib/plan';
import { downloadLine, usePhone } from '../../lib/phone';
import type { FindingsDto, PlanKind, ScanProgressSnapshotDto } from '../../types';
import type { ToastKind } from '../Toaster';
import { PhoneButton } from './Controls';
import { BackupIcon, CheckIcon, CleanUpIcon, CloseIcon, DoneIcon, LibraryIcon } from './Icons';
import { boughtSaid, PlanCard } from './ProSheet';

type Notify = (message: string, kind: ToastKind) => void;

/**
 * How far this phone has got through its first launch: Welcome, photo access (or refused it), pairing a computer,
 * the first scan, its result, the one-time offer, or through.
 */
export type FirstRunStage = 'new' | 'access' | 'refused' | 'pair' | 'scan' | 'result' | 'offer' | 'done';

const STAGE_KEY = 'photosense-first-run';
const STAGES: readonly FirstRunStage[] = ['new', 'access', 'refused', 'pair', 'scan', 'result', 'offer', 'done'];
const said = (e: unknown) => (e instanceof Error ? e.message : String(e));

function loadStage(): FirstRunStage {
  try {
    const stage = localStorage.getItem(STAGE_KEY) as FirstRunStage;
    return STAGES.includes(stage) ? stage : 'new';
  } catch { return 'new'; }
}

/** Whether first launch is at its first scan, or showing what it found, where it says so itself. */
export const firstScanShown = () => ['scan', 'result'].includes(loadStage());

function saveStage(stage: FirstRunStage) {
  // A phone that cannot remember starts its first launch again next time, which loses nothing.
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
  /** Goes to Clean up, where the first launch ends. */
  onCleanUp(): void;
  notify: Notify;
  /** About is open over everything, as when the terms of use are read from the note asking for a backup. */
  readonly reading: boolean;
  /** Opens About at the terms of use. */
  onTerms(): void;
}

/**
 * What covers the tabs until they can be used, or nothing. The first time: Welcome, why PhotoSense needs the photos and
 * the phone's own question, pairing a computer (or not, for now), the note asking for a backup unless that computer
 * says it was accepted there, the first scan, what it found, and the offer of Pro, once; it ends on Clean up. After
 * that, Scanning whenever a scan runs. The note comes at the next launch too on a phone past its first launch that has
 * not accepted it, nor heard from its computer that it was.
 */
export function useFirstRun({ scanning, progress, starting, scan, onStop, onCleanUp, notify, reading, onTerms }: FirstRunProps): React.ReactElement | null {
  const access = usePhotoAccess().data?.access;
  const note = useBackupNote();
  const [stage, go] = useState(loadStage);
  const [asking, setAsking] = useState(false);
  const [begun, setBegun] = useState(false);
  const allowed = access !== undefined && access !== 'nothing';
  // The first scan, once started, ends in its result; one already running when the first launch reached it counts too.
  if (stage === 'scan' && (scanning || starting) && !begun) setBegun(true);
  if (stage === 'scan' && begun && !scanning && !starting) {
    setBegun(false);
    go('result');
  }
  // Refused before and allowed now: the person turned access on in Settings and came back, as iOS restarts an app for.
  if (stage === 'refused' && allowed) go('pair');
  useEffect(() => saveStage(stage), [stage]);

  async function ask() {
    setAsking(true);
    try {
      const { access: now } = await askForPhotos();
      if (now === 'nothing') {
        notify('PhotoSense needs photo access to work. You can allow it later in Settings.', 'info');
        go('refused');
      } else {
        if (now === 'selected') notify('Limited access: PhotoSense checks only the photos you pick.', 'info');
        go('pair');
      }
    } catch (e) {
      notify(`PhotoSense could not ask for your photos: ${said(e)}`, 'error');
    } finally {
      setAsking(false);
    }
  }

  async function settings() {
    try { await openPhotoSettings(); } catch (e) { notify(`Settings could not be opened: ${said(e)}`, 'error'); }
  }

  // Marked begun as it starts: a scan that starts and ends before the screen is drawn again still ends in its result.
  function startFirstScan() {
    setBegun(true);
    void scan();
  }

  // After pairing, or not for now: the note first, unless the computer just said it was accepted there; then the first scan.
  async function afterPair() {
    const accepted = await note.check().catch(() => false);
    go('scan');
    if (accepted) startFirstScan();
  }

  async function accept() {
    try {
      await note.accept();
      // The first scan waited for the note.
      if (stage === 'scan' && !begun) startFirstScan();
    } catch (e) {
      notify(`PhotoSense could not keep your answer: ${said(e)}`, 'error');
    }
  }

  function finish() {
    go('done');
    onCleanUp();
  }

  if (stage === 'new') return <Welcome onStart={() => go('access')} />;
  if (stage === 'access' || stage === 'refused') return <Access busy={asking} refused={stage === 'refused'} onContinue={() => void ask()} onSettings={() => void settings()} />;
  if (stage === 'pair') return <Pair notify={notify} onNext={() => void afterPair()} />;
  if (note.due) return <BackupNote hidden={reading} onTerms={onTerms} onAccept={() => void accept()} />;
  if (stage === 'result') return <Result onOffer={() => go('offer')} onDone={finish} />;
  if (stage === 'offer') return <Offer notify={notify} onDone={finish} />;
  if (stage === 'scan' || scanning || starting) return <Scanning progress={scanning ? progress : undefined} first={stage === 'scan'} onStop={scanning ? onStop : undefined} />;
  return null;
}

/** A screen of its own over the tabs: progress dots at the top, its content, and its buttons at the foot. */
function Cover({ label, step, hidden, children, foot }: { readonly label: string; readonly step?: number; readonly hidden?: boolean; readonly children: React.ReactNode; readonly foot: React.ReactNode }) {
  return (
    <section aria-label={label} hidden={hidden}
      className="fixed inset-0 z-40 flex animate-fade-in justify-center overflow-y-auto bg-bg px-6 pb-[calc(var(--safe-b)+16px)] pt-[calc(var(--safe-t)+16px)] font-ui text-t1">
      <div className="flex w-full max-w-[440px] flex-col">
        {step !== undefined && (
          <div role="progressbar" aria-label="First steps" aria-valuemin={1} aria-valuemax={4} aria-valuenow={step + 1} className="flex gap-1.5 pb-4">
            {[0, 1, 2, 3].map(i => <span key={i} className={`h-1.5 rounded-full ${i === step ? 'w-[22px] bg-acc' : `w-1.5 ${i < step ? 'bg-acc' : 'bg-fill'}`}`} />)}
          </div>
        )}
        <div className="flex flex-1 flex-col">{children}</div>
        <div className="flex flex-col gap-1 pt-6">{foot}</div>
      </div>
    </section>
  );
}

/** What the person is holding, as first launch names it: "iPhone", "iPad", or an Android "phone" or "tablet". */
function useDevice() {
  const { ios, tablet } = usePhone();
  if (ios) return tablet ? 'iPad' : 'iPhone';
  return tablet ? 'tablet' : 'phone';
}

const Title = ({ children }: { readonly children: React.ReactNode }) => <h1 className="font-title text-[32px] font-bold leading-[1.15] tracking-[-0.02em]">{children}</h1>;
const Lead = ({ children }: { readonly children: React.ReactNode }) => <p className="text-[16.5px] leading-[1.45] text-t2 [text-wrap:pretty]">{children}</p>;

/** 01: what PhotoSense does, and Get started. */
function Welcome({ onStart }: { onStart(): void }) {
  const perks: [typeof LibraryIcon, string][] = [
    [CleanUpIcon, 'Find duplicates, screenshots and blurry shots'], [BackupIcon, 'Back up to your own computer over Wi-Fi'], [CheckIcon, 'Delete only what is already backed up'],
  ];
  return (
    <Cover label="Welcome" step={0} foot={<PhoneButton onClick={onStart}>Get started</PhoneButton>}>
      <div className="flex flex-1 flex-col justify-center gap-5">
        <img src={appIconUrl} alt="" className="h-[92px] w-[92px] rounded-[22px]" />
        <Title>PhotoSense</Title>
        <Lead>Free up space, back up to your own computer and keep your library tidy. Everything runs on your devices.</Lead>
        <ul className="flex flex-col gap-3">
          {perks.map(([Icon, text]) => (
            <li key={text} className="flex items-center gap-3 text-[15.5px] font-medium">
              <span className="grid h-9 w-9 flex-none place-items-center rounded-[10px] bg-cell text-t2"><Icon size={18} /></span>{text}
            </li>
          ))}
        </ul>
      </div>
    </Cover>
  );
}

/**
 * Keep a backup of the photos you care about, as the terms of use ask, and accept them. Kept covering the tabs, hidden,
 * while About shows the terms over it, so nothing behind it starts.
 */
function BackupNote({ hidden, onTerms, onAccept }: { readonly hidden: boolean; onTerms(): void; onAccept(): void }) {
  return (
    <Cover label={BACKUP_NOTE.title} hidden={hidden} foot={<>
      <PhoneButton onClick={onAccept}>{BACKUP_NOTE.accept}</PhoneButton>
      <PhoneButton kind="text" onClick={onTerms}>{BACKUP_NOTE.terms}</PhoneButton>
    </>}>
      <div className="flex flex-1 flex-col justify-center gap-4">
        <span className="grid h-[72px] w-[72px] place-items-center rounded-[20px] bg-acc-soft text-acc"><BackupIcon size={30} /></span>
        <Title>{BACKUP_NOTE.title}</Title>
        {BACKUP_NOTE.lines.map(line => <Lead key={line}>{line}</Lead>)}
      </div>
    </Cover>
  );
}

/** 02: why PhotoSense needs every photo, and Continue, which asks the phone's own question; once refused, where to allow it. */
function Access({ busy, refused, onContinue, onSettings }: { readonly busy: boolean; readonly refused: boolean; onContinue(): void; onSettings(): void }) {
  const device = useDevice();
  return (
    <Cover label="Photo access" step={1} foot={<>
      <PhoneButton disabled={busy} onClick={onContinue}>Continue</PhoneButton>
      {refused && <PhoneButton kind="text" onClick={onSettings}>Open Settings</PhoneButton>}
    </>}>
      <div className="flex flex-col gap-4">
        <span className="grid h-[72px] w-[72px] place-items-center rounded-[20px] bg-acc-soft text-acc"><LibraryIcon size={30} /></span>
        <Title>Let PhotoSense see your photos</Title>
        <Lead>PhotoSense needs full access to find duplicates and show what is safe to delete. Photos are checked on this {device} and never uploaded.</Lead>
        <p className="rounded-cell bg-cell px-4 py-3 text-[14px] leading-[1.45] text-t2">With limited access, PhotoSense can only check the photos you pick, so most of Clean up and Safe to delete will not work.</p>
      </div>
    </Cover>
  );
}

/**
 * 04: connecting a computer, by scanning its QR code or typing its code, or Skip for now; then what it says once paired.
 * Opened later, from Backup or Settings, it has no step dots, and Not now in place of Skip for now.
 */
export function Pair({ notify, onNext, later }: { readonly notify: Notify; onNext(): void; readonly later?: boolean }) {
  const step = later ? undefined : 2;
  const device = useDevice();
  const state = usePairing(true).data;
  const download = useAbout().data?.downloadUrl;
  const [typing, setTyping] = useState(false);
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);

  async function act(run: () => Promise<unknown>) {
    setBusy(true);
    try { await run(); } catch (e) { notify(said(e), 'error'); } finally { setBusy(false); }
  }
  const skip = () => { void stopPairing().catch(() => undefined); onNext(); };

  if (state?.stage === 'paired') {
    return (
      <Cover label="Paired" step={step} foot={<PhoneButton onClick={onNext}>Continue</PhoneButton>}>
        <div className="flex flex-1 flex-col justify-center gap-4">
          <span className="grid h-16 w-16 place-items-center rounded-full bg-keep text-on-keep"><DoneIcon /></span>
          <Title>Paired with {state.other}</Title>
          <Lead>Backups go to {state.folder} on {state.other}. You can change the folder on the computer.</Lead>
        </div>
      </Cover>
    );
  }
  if (state?.stage === 'comparing') {
    return (
      <Cover label="Compare the numbers" step={step} foot={<>
        <PhoneButton disabled={busy} onClick={() => void act(() => confirmPairing(true))}>{"It's the same number"}</PhoneButton>
        <PhoneButton kind="text" disabled={busy} onClick={() => void act(() => confirmPairing(false))}>{"It's different"}</PhoneButton>
      </>}>
        <div className="flex flex-1 flex-col justify-center gap-4">
          <Title>Check the number</Title>
          <Lead>{state.other} shows a number. Pair only if it is the same as this one.</Lead>
          <p aria-label="The number" className="font-code text-[44px] font-bold tabular-nums tracking-[.04em]">{state.check}</p>
        </div>
      </Cover>
    );
  }
  const working = state?.stage === 'scanning' || state?.stage === 'finding' || state?.stage === 'connecting';
  const doing = state?.stage === 'finding' ? 'Looking for the computer showing that code…' : state?.stage === 'connecting' ? `Connecting to ${state.other}…` : undefined;
  return (
    <Cover label="Connect your computer" step={step} foot={typing ? <>
      <PhoneButton disabled={busy || working || code.replace(/\D/g, '').length !== 6} onClick={() => void act(() => enterPairingCode(code))}>Pair</PhoneButton>
      <PhoneButton kind="text" onClick={() => setTyping(false)}>Scan the code instead</PhoneButton>
    </> : <>
      <PhoneButton disabled={busy || working} onClick={() => void act(scanPairing)}>Scan the code</PhoneButton>
      <PhoneButton kind="text" onClick={() => setTyping(true)}>Enter the code instead</PhoneButton>
      <PhoneButton kind="text" onClick={skip}>{later ? 'Not now' : 'Skip for now'}</PhoneButton>
    </>}>
      <div className="flex flex-col gap-4">
        <Title>Connect your computer</Title>
        <Lead>Backups and Safe to delete need PhotoSense on your Mac or PC. Both need to be on the same Wi-Fi.</Lead>
        <ol className="flex flex-col gap-2.5">
          {['Install PhotoSense on your Mac or PC.', 'On the computer, open Phones and choose Pair a phone.', `Point this ${device} at the code on the screen.`].map((t, i) => (
            <li key={t} className="flex items-start gap-3 text-[15px]">
              <span className="grid h-6 w-6 flex-none place-items-center rounded-full bg-fill text-[12.5px] font-semibold">{i + 1}</span>{t}
            </li>
          ))}
        </ol>
        <p className="text-[14px] leading-[1.4] text-t2">{downloadLine(download)}</p>
        {typing ? (
          <label className="flex flex-col gap-2 pt-2 text-[14px] text-t2">
            The six digits under Or enter this code on the phone
            <input inputMode="numeric" autoComplete="one-time-code" maxLength={7} value={code} onChange={e => setCode(e.target.value)} aria-label="Code from the computer"
              className="h-14 rounded-cell bg-cell px-4 text-center font-code text-[26px] tracking-[.2em] text-t1 outline-none focus:ring-2 focus:ring-acc" />
          </label>
        ) : (
          <div aria-hidden className="mx-auto grid h-[230px] w-[230px] place-items-center rounded-[28px] bg-cell">
            <div className="relative grid h-[150px] w-[150px] place-items-center text-center text-[13px] font-semibold text-white/90">
              {(['left-0 top-0 border-l-[3px] border-t-[3px] rounded-tl-[10px]', 'right-0 top-0 border-r-[3px] border-t-[3px] rounded-tr-[10px]', 'bottom-0 left-0 border-b-[3px] border-l-[3px] rounded-bl-[10px]', 'bottom-0 right-0 border-b-[3px] border-r-[3px] rounded-br-[10px]'] as const)
                .map(corner => <span key={corner} className={`absolute h-7 w-7 border-white ${corner}`} />)}
              <span className="px-4 text-t2">Point at the code on your computer</span>
            </div>
          </div>
        )}
        {doing && <p role="status" className="text-[14.5px] text-t2">{doing}</p>}
        {state?.stage === 'failed' && <p role="alert" className="text-[14.5px] text-danger-t">{state.problem}</p>}
      </div>
    </Cover>
  );
}

const KINDS: Record<FindingsDto['items'][number]['kind'], string> = { duplicates: 'Duplicates', similar: 'Similar shots', largeVideos: 'Large videos' };

/** 05, and every scan after: how many of the photos are read, what has been found so far, and that nothing leaves the phone. */
function Scanning({ progress, first, onStop }: { readonly progress?: ScanProgressSnapshotDto; readonly first: boolean; onStop?(): void }) {
  const device = useDevice();
  const found = useFound(true, true).data;
  const total = progress ? progress.primaryTotal + progress.secondaryTotal : 0;
  const done = progress ? progress.primaryProcessed + progress.secondaryProcessed : 0;
  const percent = progress && total > 0 ? Math.floor(progress.overallPercent) : 0;
  return (
    <Cover label="Scanning" step={first ? 3 : undefined} foot={<>
      {/* Drawn as the main button is, greyed: there is nothing to press until the scan is through. */}
      <p role="status" className="flex h-[var(--btn-h)] items-center justify-center rounded-full bg-acc text-[length:var(--btn-fs)] font-[number:var(--btn-fw)] text-on-acc opacity-50">Scanning…</p>
      {onStop && <PhoneButton kind="text" onClick={onStop}>Stop</PhoneButton>}
    </>}>
      <div className="flex flex-col gap-1">
        <p className="text-[14px] font-semibold text-t2">Scanning your library</p>
        <p className="font-title text-[44px] font-bold leading-tight tabular-nums">{done.toLocaleString()}</p>
        <p className="text-[15px] text-t2">{total > 0 ? `of ${total.toLocaleString()} photos and videos` : 'Counting your photos and videos…'}</p>
        <div role="progressbar" aria-label="Photos read" aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent} className="mt-3 h-2 overflow-hidden rounded-full bg-fill">
          <div className="h-full rounded-full bg-acc transition-[width] duration-500" style={{ width: `${percent}%` }} />
        </div>
        <dl className="mt-4 overflow-hidden rounded-cell bg-cell">
          {(found?.items ?? []).map((f, i) => (
            <div key={f.kind} className={`flex min-h-[46px] items-center justify-between px-4 text-[15.5px] ${i > 0 ? 'border-t-[0.5px] border-sep' : ''}`}>
              <dt>{KINDS[f.kind]}</dt><dd className="font-semibold tabular-nums">{f.count.toLocaleString()}</dd>
            </div>
          ))}
        </dl>
        <p className="mt-3 flex items-center gap-2 text-[13.5px] text-t2"><span aria-hidden className="h-[7px] w-[7px] rounded-full bg-keep" />Everything stays on this {device}.</p>
      </div>
    </Cover>
  );
}

/** 06: what the first scan found to free, the largest kinds first, and what the free plan allows; the offer follows on the free plan when there is something to free. */
function Result({ onOffer, onDone }: { onOffer(): void; onDone(): void }) {
  const device = useDevice();
  const found = useFound(true, false).data;
  const plan = usePlan().data;
  const items = [...(found?.items ?? [])].filter(f => f.count > 0).sort((a, b) => b.bytes - a.bytes).slice(0, 5);
  const freed = items.reduce((n, f) => n + f.bytes, 0);
  return (
    <Cover label="Scan complete" foot={<PhoneButton disabled={!found} onClick={() => (plan?.tier === 'free' && freed > 0 ? onOffer() : onDone())}>Continue</PhoneButton>}>
      <div className="flex flex-col gap-1">
        <p className="text-[14px] font-semibold text-keep">Scan complete</p>
        <p className="font-title text-[52px] font-bold leading-tight tabular-nums">{freed > 0 ? formatBytes(freed) : 'Nothing'}</p>
        <p className="text-[15px] text-t2">can be freed from this {device}</p>
        {items.length > 0 && (
          <dl className="mt-4 overflow-hidden rounded-cell bg-cell">
            {items.map((f, i) => (
              <div key={f.kind} className={`flex min-h-[46px] items-center gap-3 px-4 text-[15.5px] ${i > 0 ? 'border-t-[0.5px] border-sep' : ''}`}>
                <dt className="flex-1">{KINDS[f.kind]}</dt>
                <dd className="tabular-nums text-t2">{f.count.toLocaleString()}</dd>
                <dd className="w-20 text-right font-semibold tabular-nums">{formatBytes(f.bytes)}</dd>
              </div>
            ))}
          </dl>
        )}
        <p className="mt-4 rounded-cell bg-cell px-4 py-3 text-[14px] leading-[1.45] text-t2">
          The free plan removes {(plan?.removalsPerDay ?? 50).toLocaleString()} items a day, and Swipe to clean is included. Nothing is deleted until you confirm.
        </p>
      </div>
    </Cover>
  );
}

/** 07, shown once after the first scan: freeing all of it with Pro, the plans at the store's prices, and Continue with Free. */
function Offer({ notify, onDone }: { readonly notify: Notify; onDone(): void }) {
  const plan = usePlan().data;
  const found = useFound(true, false).data;
  const paired = usePairing(true).data;
  const plans = offeredPlans(plan?.offers ?? [], false);
  const [chosen, setChosen] = useState<PlanKind>('yearly');
  const [busy, setBusy] = useState(false);
  const selected = plans.find(p => p.kind === chosen) ?? plans[0];
  const freed = (found?.items ?? []).reduce((n, f) => n + f.bytes, 0);
  const computer = paired?.stage === 'paired' && paired.other ? paired.other : 'your computer';

  async function buy() {
    setBusy(true);
    try {
      const result = await buyPlan(selected!.kind);
      if (result.outcome === 'bought') { notify(boughtSaid(selected!.kind), 'ok'); onDone(); }
      else if (result.outcome === 'pending') { notify('The purchase is waiting for approval. Pro turns on once it is approved.', 'info'); onDone(); }
    } catch (e) {
      notify(said(e), 'error');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Cover label="PhotoSense Pro" foot={<>
      {selected && <PhoneButton disabled={busy} onClick={() => void buy()}>{selected.button}</PhoneButton>}
      <PhoneButton kind="text" disabled={busy} onClick={onDone}>Continue with Free</PhoneButton>
      {selected && <p className="px-2 text-center text-[12.5px] leading-[1.4] text-t2">{selected.fine}</p>}
    </>}>
      <div className="flex flex-col gap-4">
        <div className="flex justify-end">
          <button type="button" aria-label="Not now" onClick={onDone} className="grid h-9 w-9 place-items-center rounded-full bg-fill text-t2"><CloseIcon size={16} /></button>
        </div>
        <img src={appIconUrl} alt="" className="h-14 w-14 rounded-[14px]" />
        <Title>{freed > 0 ? `Free all ${formatBytes(freed)} with Pro` : 'Free it all with Pro'}</Title>
        <ul className="flex flex-col gap-2">
          {['Unlimited removals every day', `Automatic backup to ${computer}`, 'Video compression', 'Search, fix dates and the hidden vault'].map(f => (
            <li key={f} className="flex items-center gap-2.5 text-[15px]"><span className="text-acc"><CheckIcon /></span>{f}</li>
          ))}
        </ul>
        <div role="radiogroup" aria-label="Plans" className="flex flex-col gap-2">
          {plans.map(p => <PlanCard key={p.kind} plan={p} on={p.kind === selected!.kind} onChoose={() => setChosen(p.kind)} />)}
        </div>
      </div>
    </Cover>
  );
}
