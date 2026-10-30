// The answers only a phone or another device gives, stood in for in the browser: the program the tests start is a
// computer's, with no phone's photo library and no phone on the network. Everything else is the program's own.
import type { Page, Route } from '@playwright/test';

const json = (route: Route, body: unknown) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });

export interface SendState {
  stage: 'idle' | 'waiting' | 'comparing' | 'sending' | 'sent' | 'failed';
  code: string | null; computer: string | null; done: number; total: number; bytes: number; totalBytes: number;
  deletable: string[]; where: string | null; phone: string; problem: string | null;
}

/** A phone, as the page sees it: which program it is, what it may see of the photos, and sending to a computer. */
export class Phone {
  access: 'everything' | 'selected' | 'nothing';
  state: SendState = { stage: 'idle', code: null, computer: null, done: 0, total: 0, bytes: 0, totalBytes: 0, deletable: [], where: null, phone: "Jamie's iPhone", problem: null };
  /** What the page asked of the phone, but where sending has got to. */
  readonly asked: string[] = [];

  constructor(access: Phone['access'] = 'everything') { this.access = access; }

  set(changes: Partial<SendState>) { this.state = { ...this.state, ...changes }; }

  async attach(page: Page) {
    await page.route('**/api/about', async route => {
      const real = await (await route.fetch()).json();
      await json(route, { ...real, program: 'iphone' });
    });
    await page.route('**/api/library/**', async route => {
      const what = new URL(route.request().url()).pathname.split('/').pop();
      if (what !== 'access') this.asked.push(`library/${what}`);
      if (what === 'choose-more') this.access = 'everything';
      if (what === 'open-settings') return route.fulfill({ status: 204 });
      return json(route, { access: this.access });
    });
    await page.route('**/api/send/**', async route => {
      const what = new URL(route.request().url()).pathname.split('/').pop()!;
      if (what !== 'state') this.asked.push(`send/${what} ${route.request().postData() ?? ''}`.trim());
      if (what === 'start') this.set({ stage: 'waiting', total: (JSON.parse(route.request().postData() ?? '{}').files ?? []).length });
      if (what === 'stop') this.set({ stage: 'idle' });
      if (what === 'delete-sent') {
        const deleted = this.state.deletable.length;
        this.set({ deletable: [] });
        return json(route, { deleted });
      }
      return json(route, this.state);
    });
  }
}

export interface ReceiveState {
  stage: 'idle' | 'connecting' | 'comparing' | 'receiving' | 'done' | 'failed';
  code: string | null; phone: string | null; done: number; total: number; bytes: number; totalBytes: number; alreadyThere: number; folder: string | null; problem: string | null;
}

/** A phone on the network offering photos, as the computer sees it. */
export class PhoneNearby {
  state: ReceiveState = { stage: 'idle', code: null, phone: null, done: 0, total: 0, bytes: 0, totalBytes: 0, alreadyThere: 0, folder: null, problem: null };
  readonly asked: string[] = [];

  set(changes: Partial<ReceiveState>) { this.state = { ...this.state, ...changes }; }

  async attach(page: Page) {
    await page.route('**/api/phones', route => json(route, { phones: [{ key: 'wifi:phone-1', name: "Jamie's iPhone", via: 'wifi' }] }));
    await page.route('**/api/receive/**', async route => {
      const what = new URL(route.request().url()).pathname.split('/').pop()!;
      if (what !== 'state') this.asked.push(`receive/${what} ${route.request().postData() ?? ''}`.trim());
      if (what === 'start') this.set({ stage: 'connecting', phone: "Jamie's iPhone", folder: JSON.parse(route.request().postData() ?? '{}').folder });
      if (what === 'stop') this.set({ stage: 'failed', problem: 'Receiving was stopped. What had arrived is kept.' });
      return json(route, this.state);
    });
  }
}
