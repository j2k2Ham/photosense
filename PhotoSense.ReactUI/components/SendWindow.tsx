import React, { useCallback, useEffect, useState } from 'react';
import { confirmSending, deleteSent, startSending, stopSending, useSendState } from '../lib/apiClient';
import { ConfirmDialog } from './ConfirmDialog';
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

  async function remove(names: readonly string[], computer: string) {
    setBusy(true);
    try {
      const result = await deleteSent();
      if (result.deleted === 0) notify('There was nothing left to delete.', 'info');
      else notify(`Deleted ${photosAndVideos(names)} from this phone. ${names.length === 1 ? 'It is' : 'They are'} in Recently Deleted for 30 days, and the copies on ${computer} stay.`, 'ok');
      setAsking(false);
    } catch (e) {
      notify(messageOf(e), 'error');
    } finally {
      setBusy(false);
    }
  }

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
            <Buttons><button type="button" onClick={close} className="pill-outline h-11 px-5 text-[15px]">Stop</button></Buttons>
          </>
        );
      case 'sent':
        return (
          <>
            <p className="text-[15px] text-t2">
              {state.done.toLocaleString()} of {state.total.toLocaleString()} {state.total === 1 ? 'file is' : 'files are'} on <Named>{computer}</Named>
              {state.where ? <> in <Where>{state.where}</Where></> : null}. They are still on this phone too.
            </p>
            <Buttons>
              <button type="button" onClick={close} className="pill-outline h-11 px-5 text-[15px]">Close</button>
              {state.deletable.length > 0 && (
                <button type="button" onClick={() => setAsking(true)} className="pill-rose h-11 px-5 text-[15px]">Delete {photosAndVideos(state.deletable)} from this phone</button>
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
