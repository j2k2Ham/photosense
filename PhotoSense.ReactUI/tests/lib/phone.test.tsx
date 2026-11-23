import { act, render, renderHook, waitFor } from '@testing-library/react';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup } from '@testing-library/react';

import { PHONE_TOAST_MS, TOAST_MS, useToasts } from '../../components/Toaster';
import { useBackLayer } from '../../lib/backStack';
import { deviceName, deviceWord, downloadLine, platformOf, TABLET, useForm, usePhone, WORDS } from '../../lib/phone';
import { tellSystemBars } from '../../lib/systemBars';
import { width } from '../components/phone/frame';

describe('where the page is', () => {
  it('names what the person holds: a phone, an iPad, or an Android tablet', () => {
    expect([deviceName('ios', false), deviceName('android', false), deviceName('ios', true), deviceName('android', true)]).toEqual(['phone', 'phone', 'iPad', 'tablet']);
    expect([deviceWord('ios', false), deviceWord('android', false), deviceWord('ios', true), deviceWord('android', true)]).toEqual(['iPhone', 'phone', 'iPad', 'tablet']);
  });

  it('says where PhotoSense for Mac and PC comes from, or that it is coming before the web site has it', () => {
    expect(downloadLine('https://photosense.example/download')).toBe('Get PhotoSense for Mac or PC at https://photosense.example/download.');
    expect([downloadLine(null), downloadLine(undefined)]).toEqual(['PhotoSense for Mac and PC is coming soon.', 'PhotoSense for Mac and PC is coming soon.']);
  });

  it('is an iPhone or iPad, an Android phone or tablet, or a computer', () => {
    expect([platformOf('iphone'), platformOf('android'), platformOf('server'), platformOf(undefined)]).toEqual(['ios', 'android', undefined, undefined]);
    expect([WORDS.ios.put, WORDS.android.put, WORDS.android.separator]).toEqual(['Add to album', 'Move to folder', '/']);
  });

  it('is a tablet from 768 pixels wide, and changes as the width does', () => {
    const asked = vi.fn();
    const turning = width(false);
    const answer = (window.matchMedia as unknown as (q: string) => unknown);
    vi.stubGlobal('matchMedia', (q: string) => { asked(q); return answer(q); });
    const { result, unmount } = renderHook(() => useForm());
    expect(result.current).toBe('phone');
    act(() => turning.turn(true));
    expect(result.current).toBe('tablet');
    expect(asked).toHaveBeenCalledWith(TABLET);
    unmount();
  });

  it('is a phone when the page is made ahead on the server, which has no screen to measure', () => {
    width(true);
    function Probe() { return <p>{useForm()}</p>; }
    expect(renderToStaticMarkup(<Probe />)).toBe('<p>phone</p>');
  });

  it('is asked for only inside the phone layout', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    expect(() => renderHook(() => usePhone())).toThrow('A phone screen is drawn only inside the phone layout.');
  });
});

describe('Back', () => {
  // A layer closed some other way than by Back takes its entry out of the history by going back, which the browser says
  // it has done a moment later; each test closes what it opened and waits for that, so that the next does not hear it
  // as a Back of its own.
  afterEach(async () => {
    cleanup();
    await new Promise(r => setTimeout(r, 60));
  });
  function Layer({ open, onBack }: { open: boolean; onBack: () => void }) {
    useBackLayer(open, onBack);
    return null;
  }
  const settle = () => act(() => new Promise(r => setTimeout(r, 20)));

  it('closes what was opened last first, and what is closed some other way takes its entry out', async () => {
    const pushed = vi.fn(), sheet = vi.fn();
    const { rerender } = render(<><Layer open onBack={pushed} /><Layer open={false} onBack={sheet} /></>);
    const before = history.length;
    rerender(<><Layer open onBack={pushed} /><Layer open onBack={sheet} /></>);
    expect(history.length).toBe(before + 1);

    await act(async () => { history.back(); });
    await settle();
    expect([pushed.mock.calls.length, sheet.mock.calls.length]).toEqual([0, 1]);

    // The sheet, closed by Back, has no entry left to take out; the screen, closed by its own button, takes its own.
    rerender(<><Layer open onBack={pushed} /><Layer open={false} onBack={sheet} /></>);
    rerender(<><Layer open={false} onBack={pushed} /><Layer open={false} onBack={sheet} /></>);
    await settle();
    expect(pushed).not.toHaveBeenCalled();
  });

  it('something opened as another closes takes over its entry, so it stays open, and Back then closes it', async () => {
    const first = vi.fn(), next = vi.fn();
    const { rerender } = render(<><Layer open onBack={first} /><Layer open={false} onBack={next} /></>);
    const before = history.length;
    rerender(<><Layer open={false} onBack={first} /><Layer open onBack={next} /></>);
    await settle();
    expect(history.length).toBe(before);
    expect(next).not.toHaveBeenCalled();
    await act(async () => { history.back(); });
    await settle();
    expect([first.mock.calls.length, next.mock.calls.length]).toEqual([0, 1]);
  });

  it('something opened while the page is still taking an entry out is put in after, so it stays open, and Back then closes it', async () => {
    const first = vi.fn(), later = vi.fn(), brief = vi.fn();
    const three = (a: boolean, b: boolean, c: boolean) => <><Layer open={a} onBack={first} /><Layer open={b} onBack={later} /><Layer open={c} onBack={brief} /></>;
    const { rerender } = render(three(true, false, false));
    rerender(three(false, false, false));
    // The step back has started, and the browser has not yet said it is done.
    await act(async () => { await Promise.resolve(); });
    rerender(three(false, true, true));
    // One closed before it was put in leaves nothing to take out.
    rerender(three(false, true, false));
    await settle();
    expect([first.mock.calls.length, later.mock.calls.length, brief.mock.calls.length]).toEqual([0, 0, 0]);
    act(() => history.back());
    // The browser goes back when it gets to it, which under a full run's load can take a while.
    await waitFor(() => expect([first.mock.calls.length, later.mock.calls.length, brief.mock.calls.length]).toEqual([0, 1, 0]));
  });

  it('a Back that lands on an entry left over from before closes every layer made since', async () => {
    const stale = vi.fn(), open = vi.fn();
    history.pushState({ photosense: 0 }, '');
    render(<Layer open onBack={open} />);
    window.dispatchEvent(new PopStateEvent('popstate', { state: null }));
    expect(open).toHaveBeenCalledOnce();
    window.dispatchEvent(new PopStateEvent('popstate', { state: { photosense: 0 } }));
    expect(stale).not.toHaveBeenCalled();
  });
});

describe('the phone\'s own bars', () => {
  it('are told the theme by whichever app is listening, and nothing happens in a browser', () => {
    const ios = vi.fn(), android = vi.fn();
    tellSystemBars('dark');
    vi.stubGlobal('webkit', { messageHandlers: { photosenseBars: { postMessage: ios } } });
    tellSystemBars('light');
    vi.stubGlobal('PhotoSenseBars', { theme: android });
    tellSystemBars('dark');
    expect([ios.mock.calls, android.mock.calls]).toEqual([[['light'], ['dark']], [['dark']]]);
  });
});

describe('messages on a phone', () => {
  it('stay a little longer, as its own apps keep theirs', () => {
    vi.useFakeTimers();
    const { result } = renderHook(() => useToasts(PHONE_TOAST_MS));
    act(() => result.current.push('Moved 724 files to Anaconda.', 'ok'));
    act(() => vi.advanceTimersByTime(TOAST_MS));
    expect(result.current.toasts).toHaveLength(1);
    act(() => vi.advanceTimersByTime(PHONE_TOAST_MS - TOAST_MS));
    expect(result.current.toasts).toHaveLength(0);
  });
});
