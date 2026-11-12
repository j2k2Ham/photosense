// What every test starts from: PhotoSense as one program of its own, on a data folder of its own, and a folder of
// photos made for that test alone. Tests share nothing, so they run side by side.
import { test as base, expect, type APIRequestContext, type Page } from '@playwright/test';
import { spawn, type ChildProcess } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync, copyFileSync } from 'node:fs';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { dirname, join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { movie, picture, png, sidecar, type MovieOptions, type PictureOptions } from './media';

export { expect };

const here = dirname(fileURLToPath(import.meta.url));
const program = join(here, '..', '.cache', 'server', 'PhotoSense.Server.dll');

/** The program as one test has it. */
export interface Program {
  readonly url: string;
  /** Where it keeps what it records. */
  readonly data: string;
  /** What it has said so far. */
  readonly said: string[];
}

async function freePort(): Promise<number> {
  return new Promise((resolve, reject) => {
    const probe = createServer();
    probe.once('error', reject);
    probe.listen(0, '127.0.0.1', () => {
      const { port } = probe.address() as { port: number };
      probe.close(() => resolve(port));
    });
  });
}

async function started(url: string, child: ChildProcess, said: string[]) {
  for (let waited = 0; waited < 300; waited++) {
    if (child.exitCode !== null) throw new Error(`PhotoSense ended before it answered:\n${said.join('')}`);
    try { if ((await fetch(`${url}/api/health`)).ok) return; } catch { /* not yet listening */ }
    await new Promise(r => setTimeout(r, 100));
  }
  throw new Error(`PhotoSense did not answer within 30 seconds:\n${said.join('')}`);
}

const removeAll = (path: string) => { try { rmSync(path, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 }); } catch { /* a temporary folder left behind */ } };

/** A folder of photos made for one test: every path given is inside it, with / between folders. */
export class Photos {
  constructor(readonly root: string) {}

  /** The full path of something inside the folder. */
  path(inside = ''): string {
    return inside ? join(this.root, ...inside.split('/')) : this.root;
  }

  private write(inside: string, bytes: Buffer): string {
    const path = this.path(inside);
    mkdirSync(dirname(path), { recursive: true });
    writeFileSync(path, bytes);
    return path;
  }

  picture(inside: string, options: PictureOptions = {}): string { return this.write(inside, picture(options)); }
  png(inside: string, options: Parameters<typeof png>[0] = {}): string { return this.write(inside, png(options)); }
  movie(inside: string, options: MovieOptions = {}): string { return this.write(inside, movie(options)); }
  sidecar(inside: string): string { return this.write(inside, sidecar()); }

  /** A Live Photo: the picture and its video, named alike, with the identifier that makes them one. */
  livePhoto(stem: string, options: PictureOptions & MovieOptions = {}): [string, string] {
    const id = `LIVE-${stem.replace(/\W/g, '')}`;
    return [this.picture(`${stem}.JPG`, { ...options, livePhotoId: id }), this.movie(`${stem}.MOV`, { ...options, livePhotoId: id })];
  }

  /** An exact copy of a file, byte for byte. */
  copy(from: string, to: string): string {
    const target = this.path(to);
    mkdirSync(dirname(target), { recursive: true });
    copyFileSync(this.path(from), target);
    return target;
  }

  exists(inside: string): boolean { return existsSync(this.path(inside)); }

  /** Takes a file away behind PhotoSense's back, as another program might. */
  remove(inside: string) { rmSync(this.path(inside)); }

  /** Every file inside the folder now, by path with / between folders, in order. */
  files(inside = ''): string[] {
    const found: string[] = [];
    const walk = (folder: string) => {
      if (!existsSync(folder)) return;
      for (const name of readdirSync(folder)) {
        const path = join(folder, name);
        if (statSync(path).isDirectory()) walk(path); else found.push(relative(this.root, path).split(sep).join('/'));
      }
    };
    walk(this.path(inside));
    return found.sort();
  }

  bytes(inside: string): Buffer { return readFileSync(this.path(inside)); }
}

/** Asking the program things directly, to set a test up or see what it did, as the page would. */
export class Service {
  constructor(private readonly request: APIRequestContext, readonly url: string) {}

  private readonly headers = { 'x-photosense-client': 'e2e' };

  async get<T>(path: string): Promise<T> {
    const answer = await this.request.get(`${this.url}/api/${path}`, { headers: this.headers });
    expect(answer.ok(), `${path}: ${answer.status()} ${await answer.text()}`).toBeTruthy();
    return answer.json() as Promise<T>;
  }

  async post<T>(path: string, body?: unknown): Promise<T> {
    const answer = await this.request.post(`${this.url}/api/${path}`, { headers: this.headers, data: body });
    expect(answer.ok(), `${path}: ${answer.status()} ${await answer.text()}`).toBeTruthy();
    return (answer.headers()['content-type']?.includes('json') ? answer.json() : answer.text()) as Promise<T>;
  }

  /** Scans a folder, from scratch, and waits for the scan to finish. */
  async scan(root: string, options: { secondary?: string; recursive?: boolean } = {}) {
    await this.post('scan/start', { primaryLocation: root, secondaryLocation: options.secondary ?? '', recursive: options.recursive ?? true, startOver: true });
    await expect.poll(async () => (await this.get<{ completed?: string | null }>('scan/status')).completed ?? null, { timeout: 60_000, message: 'the scan finishes' }).not.toBeNull();
  }
}

export const test = base.extend<{ program: Program; photos: Photos; service: Service }>({
  program: async ({}, use, testInfo) => {
    const port = await freePort();
    const data = mkdtempSync(join(tmpdir(), 'photosense-e2e-data-'));
    const url = `http://localhost:${port}`;
    const said: string[] = [];
    const child = spawn('dotnet', [program, '--urls', url], {
      cwd: dirname(program),
      env: { ...process.env, PhotoStorage__DatabasePath: join(data, 'photosense.db'), PhotoStorage__ThumbnailPath: join(data, 'thumbnails'), DOTNET_NOLOGO: '1' },
    });
    child.stdout?.on('data', chunk => said.push(String(chunk)));
    child.stderr?.on('data', chunk => said.push(String(chunk)));
    try {
      await started(url, child, said);
      await use({ url, data, said });
    } finally {
      child.kill();
      await new Promise(r => (child.exitCode !== null ? r(null) : child.once('exit', r)));
      if (testInfo.status !== testInfo.expectedStatus) await testInfo.attach('PhotoSense said', { body: said.join(''), contentType: 'text/plain' });
      removeAll(data);
    }
  },
  baseURL: async ({ program }, use) => use(program.url),
  photos: async ({}, use) => {
    const root = mkdtempSync(join(tmpdir(), 'photosense-e2e-photos-'));
    await use(new Photos(join(root, 'Phone Pictures')));
    removeAll(root);
  },
  service: async ({ request, program }, use) => use(new Service(request, program.url)),
});

/** The message PhotoSense shows after something is done. */
export const message = (page: Page, text: string | RegExp) => page.getByRole('log', { name: 'Messages' }).getByText(text);
