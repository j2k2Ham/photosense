import { StrictMode } from 'react';
import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { deferred } from '../fakeSignalR';
import { reply } from '../fixtures';
import { useScanLogs, type UseScanLogsOptions } from '../../lib/useScanLogs';

const hub = await vi.hoisted(async () => new (await import('../fakeSignalR')).FakeHub());
vi.mock('@microsoft/signalr', () => hub.module);

const ROOT = 'http://localhost:7071';
const hubOffer = { url: 'https://hub.example/client', accessToken: 'token-1' };
const fetchMock = vi.fn<typeof fetch>();
// What the service answers to each of the two requests the hook makes; a test swaps these.
let negotiation: () => Response | Promise<Response>;
let polled: () => Response | Promise<Response>;

beforeEach(() => {
  vi.useFakeTimers();
  hub.reset();
  negotiation = () => reply(hubOffer);
  polled = () => reply({ items: [], count: 0 });
  fetchMock.mockReset();
  fetchMock.mockImplementation(async url => (String(url).endsWith('/negotiate') ? negotiation() : polled()));
  vi.stubGlobal('fetch', fetchMock);
});

const start = (options?: UseScanLogsOptions) => renderHook(() => useScanLogs(options));
const settle = (ms = 0) => act(async () => { await vi.advanceTimersByTimeAsync(ms); });
const requests = (part: string) => fetchMock.mock.calls.map(c => String(c[0])).filter(url => url.includes(part));
const polls = () => requests('/api/scan/logs?');
const negotiations = () => requests('/negotiate');
const line = (n: number, instanceId?: string) => ({ instanceId, timestamp: `12:00:${String(n).padStart(2, '0')}`, level: 'Info', message: `line ${n}` });
const refused = (body: string | undefined, status: number) => () => reply(body, status);

describe('over the hub', () => {
  it('connects, then streams each line as it is sent', async () => {
    const offer = deferred<Response>();
    negotiation = () => offer.promise;

    const { result } = start();
    await settle();
    expect(result.current).toMatchObject({ status: 'connecting', logs: [], error: undefined });
    expect(fetchMock).toHaveBeenLastCalledWith(`${ROOT}/api/scan/logs/negotiate`, { method: 'POST' });

    offer.resolve(reply(hubOffer));
    await settle();
    expect(result.current.status).toBe('streaming');
    expect(hub.connection.url).toBe('https://hub.example/client');
    expect(hub.connection.options.accessTokenFactory()).toBe('token-1');

    act(() => {
      hub.connection.log('scan-7', '12:00:01', 'Info', 'Scanning 12 files');
      hub.connection.log('scan-7', '12:00:02', 'Warn', 'Could not decode a.heic');
    });
    expect(result.current.logs).toEqual([
      { instanceId: 'scan-7', timestamp: '12:00:01', level: 'Info', message: 'Scanning 12 files' },
      { instanceId: 'scan-7', timestamp: '12:00:02', level: 'Warn', message: 'Could not decode a.heic' },
    ]);

    // The hub is used instead of polling, never beside it.
    await settle(10_000);
    expect(polls()).toHaveLength(0);
    expect(result.current.error).toBeUndefined();
  });

  it('keeps only the newest lines once the list is full', async () => {
    const { result } = start({ maxItems: 3 });
    await settle();

    act(() => { for (let n = 1; n <= 5; n++) hub.connection.log('scan-7', `t${n}`, 'Info', `line ${n}`); });

    expect(result.current.logs.map(l => l.message)).toEqual(['line 3', 'line 4', 'line 5']);
  });

  it('empties the list on request', async () => {
    const { result } = start();
    await settle();
    act(() => hub.connection.log('scan-7', 't1', 'Info', 'line 1'));
    expect(result.current.logs).toHaveLength(1);

    act(() => result.current.clear());

    expect(result.current.logs).toEqual([]);
    expect(result.current.status).toBe('streaming');
  });

  it('carries on by polling when the hub closes for good', async () => {
    const { result } = start();
    await settle();

    polled = () => reply({ items: [line(1, 'scan-7')] });
    act(() => hub.connection.close());
    await settle();

    expect(result.current.status).toBe('polling');
    expect(result.current.logs.map(l => l.message)).toEqual(['line 1']);
  });

  it('makes one connection under React\'s strict mode, which starts everything twice', async () => {
    const { result } = renderHook(() => useScanLogs(), { wrapper: StrictMode });
    await settle();

    expect(result.current.status).toBe('streaming');
    expect(hub.connections).toHaveLength(1);
    act(() => hub.connection.log('scan-7', 't1', 'Info', 'line 1'));
    expect(result.current.logs).toHaveLength(1);
  });
});

describe('by polling', () => {
  it('takes over when the hub is refused, and says why', async () => {
    negotiation = refused('SignalR is not configured', 500);
    polled = () => reply({ items: [line(1, 'scan-7'), line(2)], count: 2 });

    const { result } = start();
    await settle();

    expect(result.current.status).toBe('polling');
    expect(result.current.error).toBe('SignalR is not configured');
    expect(polls()).toEqual([`${ROOT}/api/scan/logs?limit=200`]);
    expect(result.current.logs).toEqual([
      { instanceId: 'scan-7', timestamp: '12:00:01', level: 'Info', message: 'line 1' },
      { instanceId: 'n/a', timestamp: '12:00:02', level: 'Info', message: 'line 2' },      // the service did not say which scan
    ]);
    expect(hub.connections).toHaveLength(0);
  });

  it('gives a reason of its own when the refusal comes with none', async () => {
    negotiation = refused('', 503);
    const { result } = start();
    await settle();
    expect(result.current).toMatchObject({ status: 'polling', error: 'negotiate failed' });
  });

  it('takes over when the hub is offered but cannot be reached', async () => {
    hub.startError = new Error('WebSocket failed to connect');
    const { result } = start();
    await settle();
    expect(result.current).toMatchObject({ status: 'polling', error: 'WebSocket failed to connect' });
    expect(polls()).toHaveLength(1);
  });

  it('is used from the start when the hub is switched off', async () => {
    const { result } = start({ disableSignalR: true });
    await settle();
    expect(result.current).toMatchObject({ status: 'polling', error: 'SignalR disabled' });
    expect(negotiations()).toHaveLength(0);
  });

  it('asks again every second and a half, through answers with nothing new', async () => {
    negotiation = refused(undefined, 500);
    const answers = [{ items: [line(1)] }, { count: 0 }, { items: [] }, { items: [line(2)] }];
    polled = () => reply(answers.shift());

    const { result } = start();
    await settle();
    expect(result.current.logs).toHaveLength(1);

    await settle(1499);
    expect(polls()).toHaveLength(1);
    await settle(1);
    expect(polls()).toHaveLength(2);
    await settle(1500);
    expect(result.current.logs).toHaveLength(1);
    await settle(1500);
    expect(result.current.logs.map(l => l.message)).toEqual(['line 1', 'line 2']);
  });

  it('asks as often, and for as many lines, as it is told to', async () => {
    const { result } = start({ disableSignalR: true, pollIntervalMs: 100, restLimit: 50, maxItems: 2 });
    polled = () => reply({ items: [line(1), line(2), line(3)] });
    await settle();
    expect(polls()).toEqual([`${ROOT}/api/scan/logs?limit=50`]);
    expect(result.current.logs.map(l => l.message)).toEqual(['line 2', 'line 3']);

    await settle(100);
    expect(polls()).toHaveLength(2);
  });

  it('keeps the newest five hundred lines unless told otherwise', async () => {
    polled = () => reply({ items: Array.from({ length: 501 }, (_, n) => line(n)) });
    const { result } = start({ disableSignalR: true });
    await settle();
    expect(result.current.logs).toHaveLength(500);
    expect(result.current.logs[0].message).toBe('line 1');
  });

  it('waits twice as long, once, after being told to slow down', async () => {
    const answers = [reply('Rate limit exceeded', 429), reply({ items: [line(1)] }), reply({ items: [line(2)] })];
    polled = () => answers.shift()!;

    const { result } = start({ disableSignalR: true, pollIntervalMs: 1000 });
    await settle();
    expect(polls()).toHaveLength(1);

    await settle(1999);
    expect(polls()).toHaveLength(1);
    await settle(1);
    expect(polls()).toHaveLength(2);
    await settle(1000);
    expect(result.current.logs.map(l => l.message)).toEqual(['line 1', 'line 2']);
  });

  it('tries again at the usual pace after any other refusal', async () => {
    const answers = [reply('The database is busy', 500), reply({ items: [line(1)] })];
    polled = () => answers.shift()!;

    const { result } = start({ disableSignalR: true });
    await settle();
    expect(result.current.logs).toEqual([]);

    await settle(1500);
    expect(result.current.logs).toHaveLength(1);
  });

  it.each([
    [new TypeError('Failed to fetch'), 'Failed to fetch'],
    ['the network is down', 'the network is down'],
  ])('reports a lost connection and keeps trying', async (failure, message) => {
    let attempts = 0;
    polled = () => { if (attempts++ === 0) throw failure; return reply({ items: [line(1)] }); };

    const { result } = start({ disableSignalR: true });
    await settle();
    expect(result.current).toMatchObject({ status: 'polling', error: message, logs: [] });

    await settle(1500);
    expect(result.current.logs).toHaveLength(1);
  });
});

describe('pausing', () => {
  it('holds everything back when started paused, until resumed', async () => {
    const { result } = start({ startPaused: true });
    await settle(10_000);
    expect(result.current.status).toBe('paused');
    expect(fetchMock).not.toHaveBeenCalled();

    act(() => result.current.resume());
    await settle();

    expect(result.current.status).toBe('streaming');
  });

  it('closes the hub, and ignores whatever arrives afterwards', async () => {
    const { result } = start();
    await settle();
    const connection = hub.connection;

    act(() => result.current.pause());
    expect(result.current.status).toBe('paused');
    expect(connection.stop).toHaveBeenCalledOnce();

    act(() => {
      connection.log('scan-7', 't1', 'Info', 'a line already on its way');
      connection.close();
    });
    await settle(10_000);
    expect(result.current.logs).toEqual([]);
    expect(polls()).toHaveLength(0);

    // A second pause finds nothing left to stop.
    act(() => result.current.pause());
    expect(connection.stop).toHaveBeenCalledOnce();
  });

  it('is not troubled by a hub that will not stop cleanly', async () => {
    hub.stopError = new Error('already closed');
    const { result } = start();
    await settle();

    act(() => result.current.pause());
    await settle();

    expect(result.current.status).toBe('paused');
  });

  it('stops the polling, and resuming starts over with the hub', async () => {
    negotiation = refused(undefined, 500);
    const { result } = start();
    await settle();
    expect(polls()).toHaveLength(1);

    act(() => result.current.pause());
    await settle(10_000);
    expect(polls()).toHaveLength(1);

    negotiation = () => reply(hubOffer);
    act(() => result.current.resume());
    await settle();
    expect(result.current.status).toBe('streaming');
    expect(negotiations()).toHaveLength(2);
  });

  it('leaves a running stream alone when resumed without having been paused', async () => {
    const { result } = start();
    await settle();

    act(() => result.current.resume());
    await settle();

    expect(result.current.status).toBe('streaming');
    expect(negotiations()).toHaveLength(1);
    expect(hub.connections).toHaveLength(1);
  });

  it('while the hub is still being negotiated: the offer is not taken up', async () => {
    const offer = deferred<Response>();
    negotiation = () => offer.promise;
    const { result } = start();
    await settle();

    act(() => result.current.pause());
    offer.resolve(reply(hubOffer));
    await settle();

    expect(result.current.status).toBe('paused');
    expect(hub.connections).toHaveLength(0);
  });

  it('while the hub is still being negotiated: a refusal does not start the polling', async () => {
    const offer = deferred<Response>();
    negotiation = () => offer.promise;
    const { result } = start();
    await settle();

    act(() => result.current.pause());
    offer.resolve(reply('SignalR is not configured', 500));
    await settle(10_000);

    expect(result.current).toMatchObject({ status: 'paused', error: undefined });
    expect(polls()).toHaveLength(0);
  });

  it('while the hub is still opening: it is closed as soon as it has opened', async () => {
    const opening = deferred();
    hub.starting = opening.promise;
    const { result } = start();
    await settle();
    expect(result.current.status).toBe('connecting');

    act(() => result.current.pause());
    opening.resolve();
    await settle();

    expect(result.current.status).toBe('paused');
    expect(hub.connection.stop).toHaveBeenCalledOnce();
  });

  it.each([
    ['an answer', () => reply({ items: [line(1)] })],
    ['a failure', () => { throw new TypeError('Failed to fetch'); }],
  ])('while a poll is in flight: %s that comes back late is dropped', async (_what, outcome) => {
    const inFlight = deferred();
    polled = async () => { await inFlight.promise; return outcome(); };
    const { result } = start({ disableSignalR: true });
    await settle();
    expect(polls()).toHaveLength(1);

    act(() => result.current.pause());
    inFlight.resolve();
    await settle(10_000);

    expect(result.current).toMatchObject({ status: 'paused', logs: [], error: 'SignalR disabled' });
    expect(polls()).toHaveLength(1);
  });
});

describe('when the component goes away', () => {
  it('closes the hub', async () => {
    const { unmount } = start();
    await settle();

    unmount();

    expect(hub.connection.stop).toHaveBeenCalledOnce();
  });

  it('stops the polling', async () => {
    const { unmount } = start({ disableSignalR: true });
    await settle();
    expect(polls()).toHaveLength(1);

    unmount();
    await vi.advanceTimersByTimeAsync(10_000);

    expect(polls()).toHaveLength(1);
  });
});
