import React, { useCallback, useEffect, useRef, useState } from 'react';
import { confirmSending, deleteSent, startSending, stopSending, useSendState } from '../lib/apiClient';
import { ConfirmDialog } from './ConfirmDialog';
import { Segmented } from './ModeSwitch';
import type { ToastKind } from './Toaster';
import { Buttons, Compare, messageOf, Named, Progress, TransferWindow, Where } from './TransferParts';

interface Props {
  /** The files chosen in Organize, by the ids it gave them. */
  readonly files: readonly string[];
  notify(message: string, kind: ToastKind): void;
  onClose(): void;
}

// While the phone waits for the computer, compares numbers with it or sends, closing the window stops it.
const LIVE = new Set(['waiting', 'comparing', 'sending']);
const VIDEO = /\.(mov|mp4|m4v)$/i;

/** "2 photos and 1 video", by the names of what goes. */
export function photosAndVideos(names: readonly string[]): string {
  const videos = names.filter(n => VIDEO.test(n)).length, photos = names.length - videos;
  const parts = [photos > 0 && `${photos.toLocaleString()} ${photos === 1 ? 'photo' : 'photos'}`, videos > 0 && `${videos.toLocaleString()} ${videos === 1 ? 'video' : 'videos'}`];
  return parts.filter(Boolean).join(' and ');
}

/** Up to three names, then how many more: "IMG_0001.JPG, IMG_0002.JPG, IMG_0003.JPG and 4 more". */
export function someNames(names: readonly string[]): string {
  const shown = names.slice(0, 3), more = names.length - shown.length;
  if (more > 0) return `${shown.join(', ')} and ${more.toLocaleString()} more`;
  return shown.length > 1 ? `${shown.slice(0, -1).join(', ')} and ${shown[shown.length - 1]}` : shown.join('');
}

/** A phone offering the photos chosen in Organize to PhotoSense on a computer, and deleting them from the phone once they are there. */
export function SendWindow({ files, notify, onClose }: Props) {
  const [started, setStarted] = useState(false);
  const [refused, setRefused] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [asking, setAsking] = useState(false);
  // Copy keeps the photos on the phone; Move deletes them from it once all of each is safely on the computer.
  const [mode, setMode] = useState<'copy' | 'move'>('copy');
  // A move deletes once, by itself; should the person say no to the phone's question, the button is there to try again.
  const moving = useRef(false);
  const [notMoved, setNotMoved] = useState(false);
  const { data } = useSendState();

  const start = useCallback(() => { startSending([...files]).then(() => setStarted(true), e => setRefused(messageOf(e))); }, [files]);
  useEffect(start, [start]);
  const offer = () => {
    setRefused(undefined);
    start();
  };

  const state = started ? data : undefined;
  const close = useCallback(() => {
    if (state && LIVE.has(state.stage)) stopSending().catch(() => undefined);
    onClose();
  }, [state, onClose]);

  async function answer(same: boolean) {
    setBusy(true);
    try { await confirmSending(same); }
    catch (e) { notify(messageOf(e), 'error'); }
    finally { setBusy(false); }
  }

  const said = useCallback((result: { deleted: number }, names: readonly string[], computer: string, moved: boolean) => {
    const they = names.length === 1 ? 'It is' : 'They are';
    if (result.deleted === 0) notify('There was nothing left to delete.', 'info');
    else if (moved) notify(`Moved ${photosAndVideos(names)} to ${computer}. ${they} in Recently Deleted on this phone for 30 days.`, 'ok');
    else notify(`Deleted ${photosAndVideos(names)} from this phone. ${they} in Recently Deleted for 30 days, and the copies on ${computer} stay.`, 'ok');
  }, [notify]);

  async function remove(names: readonly string[], computer: string) {
    setBusy(true);
    try {
      said(await deleteSent(), names, computer, false);
      setAsking(false);
    } catch (e) {
      notify(messageOf(e), 'error');
    } finally {
      setBusy(false);
    }
  }

  const movable = mode === 'move' && state?.stage === 'sent' && state.deletable.length > 0 && !notMoved;
  useEffect(() => {
    if (!movable || moving.current) return;
    moving.current = true;
    const names = state!.deletable, computer = state!.computer!;
    deleteSent().then(result => said(result, names, computer, true), e => {
      notify(messageOf(e), 'error');
      setNotMoved(true);
    });
  }, [movable, state, said, notify]);

  const choice = (
    <div className="flex flex-col gap-1.5">
      <Segmented<'copy' | 'move'> label="Copy or move" size="wide" value={mode} onChange={setMode} options={[{ value: 'copy', name: 'Copy' }, { value: 'move', name: 'Move' }]} />
      <p className="text-[13px] text-t2">
        {mode === 'copy'
          ? 'The photos stay on this phone as well.'
          : 'Once all of each photo is safely on the computer, it is deleted from this phone, into Recently Deleted for 30 days. The phone asks you first.'}
      </p>
    </div>
  );

  function body() {
    if (refused) {
      return (
        <>
          <p role="alert" className="text-[15px] text-rose-t">{refused}</p>
          <Buttons>
            <button type="button" onClick={close} className="pill-outline h-11 px-5 text-[15px]">Close</button>
            <button type="button" onClick={offer} className="pill-brand h-11 px-5 text-[15px]">Try again</button>
          </Buttons>
        </>
      );
    }
    if (!state) return <p className="text-[15px] text-t2">Getting ready…</p>;
    const computer = state.computer ?? 'the computer';
    switch (state.stage) {
      case 'comparing':
        return <Compare code={state.code!} other={computer} busy={busy} onAnswer={same => void answer(same)} />;
      case 'sending':
        return (
          <>
            <p className="text-[15px] text-t2">Sending to <Named>{computer}</Named>. Keep PhotoSense open on this phone until it is done.</p>
            <Progress label={`Sending to ${computer}`} done={state.done} total={state.total} bytes={state.bytes} totalBytes={state.totalBytes} />
            {choice}
            <Buttons><button type="button" onClick={close} className="pill-outline h-11 px-5 text-[15px]">Stop</button></Buttons>
          </>
        );
      case 'sent':
        if (movable) return <p className="text-[15px] text-t2">All {state.done.toLocaleString()} {state.done === 1 ? 'file is' : 'files are'} on <Named>{computer}</Named>. Deleting them from this phone; the phone asks you first.</p>;
        return (
          <>
            <p className="text-[15px] text-t2">
              {state.done.toLocaleString()} of {state.total.toLocaleString()} {state.total === 1 ? 'file is' : 'files are'} on <Named>{computer}</Named>
              {state.where ? <> in <Where>{state.where}</Where></> : null}.{state.deletable.length > 0 && ' They are still on this phone too.'}
            </p>
            <Buttons>
              <button type="button" onClick={close} className="pill-outline h-11 px-5 text-[15px]">Close</button>
              {state.deletable.length > 0 && (
                // On a phone's narrow screen the whole of what it does wraps onto two lines rather than leave the window.
                <button type="button" onClick={() => setAsking(true)} className="pill-rose min-h-11 max-w-full whitespace-normal px-5 py-2.5 text-[15px]">Delete {photosAndVideos(state.deletable)} from this phone</button>
              )}
            </Buttons>
          </>
        );
      case 'failed':
        return (
          <>
            <p role="alert" className="text-[15px] text-rose-t">{state.problem}</p>
            <Buttons>
              <button type="button" onClick={close} className="pill-outline h-11 px-5 text-[15px]">Close</button>
              <button type="button" onClick={offer} className="pill-brand h-11 px-5 text-[15px]">Try again</button>
            </Buttons>
          </>
        );
      case 'idle':
        return (
          <>
            <p className="text-[15px] text-t2">This phone is not offering photos now.</p>
            <Buttons>
              <button type="button" onClick={close} className="pill-outline h-11 px-5 text-[15px]">Close</button>
              <button type="button" onClick={offer} className="pill-brand h-11 px-5 text-[15px]">Offer them again</button>
            </Buttons>
          </>
        );
      default:
        return (
          <>
            <p className="text-[15px] text-t2">
              Waiting for your computer. On the computer, open PhotoSense and choose <Named>Receive from phone</Named> in the menu. This phone shows there as <Named>{state.phone}</Named>.
            </p>
            <p className="text-[13.5px] text-t2">
              {state.total.toLocaleString()} {state.total === 1 ? 'file is' : 'files are'} offered, with any Live Photo videos and edits. The phone and the computer need to be on the same Wi-Fi, or the phone plugged into the computer with a cable. Keep PhotoSense open on this phone; the screen stays on meanwhile.
            </p>
            {choice}
            <Buttons><button type="button" onClick={close} className="pill-outline h-11 px-5 text-[15px]">Stop</button></Buttons>
          </>
        );
    }
  }

  return (
    <>
      <TransferWindow title="Send to computer" frozen={asking} onClose={close}>{body()}</TransferWindow>
      {asking && state && (
        <ConfirmDialog busy={busy} title={state.deletable.length === 1 ? `Delete ${state.deletable[0]} from this phone?` : `Delete ${photosAndVideos(state.deletable)} from this phone?`}
          confirmLabel={`Delete ${photosAndVideos(state.deletable)}`} onCancel={() => setAsking(false)} onConfirm={() => void remove(state.deletable, state.computer!)}
          note={<>The copies on <Named>{state.computer!}</Named>{state.where ? <> in <Where>{state.where}</Where></> : null} stay.</>}>
          <p><Named>{someNames(state.deletable)}</Named> will go to Recently Deleted on this phone, with any Live Photo videos and edits. The phone keeps them there for 30 days, and asks you first.</p>
        </ConfirmDialog>
      )}
    </>
  );
}
