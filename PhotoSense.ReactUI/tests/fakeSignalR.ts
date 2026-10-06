import { vi } from 'vitest';

/** One hub connection as the code under test sees it, with handles for the test to drive it. */
export class FakeConnection {
  readonly handlers = new Map<string, (...args: string[]) => void>();
  private closeHandler?: () => void;
  readonly start = vi.fn(async () => {
    await this.hub.starting;
    if (this.hub.startError) throw this.hub.startError;
  });
  readonly stop = vi.fn(async () => {
    if (this.hub.stopError) throw this.hub.stopError;
  });

  constructor(private readonly hub: FakeHub, readonly url: string, readonly options: { accessTokenFactory(): string }) {}

  on(name: string, handler: (...args: string[]) => void) { this.handlers.set(name, handler); }
  onclose(handler: () => void) { this.closeHandler = handler; }

  /** The server sends a log line. */
  log(instanceId: string, timestamp: string, level: string, message: string) { this.handlers.get('log')!(instanceId, timestamp, level, message); }
  /** The connection is lost for good. */
  close() { this.closeHandler!(); }
}

export class FakeHub {
  /** Every connection built so far, oldest first. */
  connections: FakeConnection[] = [];
  /** Makes start() fail. */
  startError?: Error;
  /** Makes stop() fail. */
  stopError?: Error;
  /** Holds start() back until it resolves. */
  starting: Promise<void> = Promise.resolve();

  get connection(): FakeConnection { return this.connections[this.connections.length - 1]; }

  reset() {
    this.connections = [];
    this.startError = undefined;
    this.stopError = undefined;
    this.starting = Promise.resolve();
  }

  /** Stands in for the @microsoft/signalr module. */
  readonly module = (() => {
    const hub = this;
    return {
      HubConnectionBuilder: class {
        private url = '';
        private options = { accessTokenFactory: () => '' };
        withUrl(url: string, options: { accessTokenFactory(): string }) { this.url = url; this.options = options; return this; }
        withAutomaticReconnect() { return this; }
        build() {
          const connection = new FakeConnection(hub, this.url, this.options);
          hub.connections.push(connection);
          return connection;
        }
      },
    };
  })();
}

/** A promise a test settles when it chooses, for holding a request open. */
export function deferred<T = void>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
  return { promise, resolve, reject };
}
