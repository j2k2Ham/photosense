import type { PlanDto, PlanKind, PlanOfferDto } from '../types';

/** The plans, in the order they are offered. */
export const PLAN_KINDS: readonly PlanKind[] = ['yearly', 'monthly', 'lifetime', 'family'];

/** What opened the Pro sheet, which says why it is there. */
export type ProReason = 'limit' | 'auto' | 'compress' | 'search' | 'fix' | 'vault' | 'location' | 'server' | 'organize' | 'trips' | 'none';

/** What Pro adds, as the Pro sheet lists it. */
export const PRO_FEATURES: readonly string[] = ['Unlimited removals every day', 'Automatic backup to your computer', 'Video compression'];

/** What Pro adds, as the Pro sheet lists it when it opens for a second location or Connect to server. */
export const LOCATION_FEATURES: readonly string[] = ['Back up to as many locations as you like', 'Your own servers: S3, Azure, WebDAV and SFTP', 'Automatic backup', 'Unlimited removals every day'];

/** What a trial includes, as the Subscription page lists it. */
export const TRIAL_FEATURES: readonly string[] = [
  'Unlimited removals every day', 'Automatic backup and video compression', 'Search for what is in photos', 'Fix dates and places, and the hidden vault',
];

/** The Free and Pro comparison on the Subscription page: what each row is, then Free's and Pro's answer (true for a tick, null for none). */
export const COMPARISON: readonly (readonly [string, string | boolean | null, string | boolean])[] = [
  ['Full scan and Swipe to clean', true, true],
  ['Removals each day', '50', 'Unlimited'],
  ['Send photos to your computer', true, true],
  ['Automatic backup', null, true],
  ['Video compression', null, true],
  ['Search what is in photos', null, true],
  ['Fix dates and places', null, true],
  ['Hidden vault', null, true],
];

/** The Free and Pro comparison, with the free plan's own number of removals each day. */
export const comparison = (perDay: number) => COMPARISON.map(([what, free, pro]) => [what, what === 'Removals each day' ? perDay.toLocaleString() : free, pro] as const);

/** A plan as offered: its name, the line under it, its price, a badge, what its button says, and its fine print. */
export interface OfferedPlan {
  readonly kind: PlanKind;
  readonly name: string;
  readonly sub: string;
  readonly price: string;
  readonly badge?: string;
  readonly button: string;
  readonly fine: string;
}

const NAMES: Record<PlanKind, string> = { yearly: 'Yearly', monthly: 'Monthly', lifetime: 'Lifetime', family: 'Family' };

/** What a plan is called: "Yearly". */
export const planName = (kind: PlanKind) => NAMES[kind];

function money(amount: number, currency: string): string {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency, minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount);
}

/**
 * The plans for sale, each with the store's own price and the words the design gives it. Every number comes from the
 * store: the monthly cost of the yearly plan and how much it saves are worked out from its prices. A plan the store has
 * not priced is left out, and Family is offered only where asked for.
 */
export function offeredPlans(offers: readonly PlanOfferDto[], withFamily: boolean): OfferedPlan[] {
  const of = (kind: PlanKind) => offers.find(o => o.kind === kind);
  const monthly = of('monthly');
  const plans: OfferedPlan[] = [];
  for (const kind of PLAN_KINDS) {
    const offer = of(kind);
    if (!offer || (kind === 'family' && !withFamily)) continue;
    const price = offer.price;
    if (kind === 'yearly') {
      const saves = monthly && monthly.currency === offer.currency ? Math.round((1 - offer.amount / (monthly.amount * 12)) * 100) : 0;
      plans.push({
        kind, name: NAMES[kind], sub: `7-day free trial, then ${money(offer.amount / 12, offer.currency)} a month`, price, badge: saves > 0 ? `Save ${saves}%` : undefined,
        button: 'Start 7-day free trial', fine: `Free for 7 days, then ${price} a year. We remind you 2 days before the trial ends. Cancel any time in Settings.`,
      });
    } else if (kind === 'monthly') {
      plans.push({ kind, name: NAMES[kind], sub: 'Billed every month', price, button: `Subscribe for ${price} a month`, fine: 'Renews every month. Cancel any time in Settings.' });
    } else if (kind === 'lifetime') {
      plans.push({ kind, name: NAMES[kind], sub: 'Pay once', price, button: `Buy for ${price}`, fine: 'One payment, no renewal.' });
    } else {
      plans.push({ kind, name: NAMES[kind], sub: 'Pro for up to 6 people', price, button: `Start Family for ${price} a year`, fine: 'Renews every year. Cancel any time in Settings.' });
    }
  }
  return plans;
}

/** What the Pro sheet is opened with. */
export interface ProAsk {
  readonly reason: ProReason;
  /** For the limit: how many were chosen, and what they are called ("items", "duplicates"). */
  readonly count?: number;
  readonly label?: string;
  /** For a second location besides the computer: the one that is on. */
  readonly current?: string;
  /** For a second location: backups have gone to the one that is on, so the free plan is held to it. */
  readonly held?: boolean;
}

/** The line at the top of the Pro sheet that says why it opened; none when it was opened for its own sake. */
export function contextLine(ask: ProAsk, plan: Pick<PlanDto, 'leftToday' | 'removalsPerDay'>): string | undefined {
  switch (ask.reason) {
    case 'limit': {
      const left = plan.leftToday ?? 0;
      const count = ask.count ?? 0;
      // "1 duplicate", "2 duplicates": the label is given for many.
      const what = count === 1 ? (ask.label ?? 'items').replace(/s$/, '') : ask.label ?? 'items';
      return left > 0
        ? `You chose ${count.toLocaleString()} ${what}. The free plan deletes ${plan.removalsPerDay.toLocaleString()} items a day, and you have ${left.toLocaleString()} left today.`
        : `You have used today's ${plan.removalsPerDay.toLocaleString()} free removals. More are available tomorrow.`;
    }
    case 'auto': return 'Automatic backup is part of Pro. Sending photos yourself stays free.';
    case 'compress': return 'Video compression is part of Pro.';
    case 'search': return 'Searching for what is in photos is part of Pro. Searching by date and place stays free.';
    case 'fix': return 'Fixing dates, places and file names is part of Pro.';
    case 'vault': return 'The hidden vault is part of Pro.';
    case 'location': return ask.held
      ? `The free plan backs up to your computer and one other location, and your backups are on ${ask.current}. Pro adds as many as you like.`
      : `The free plan backs up to your computer and one other location, and ${ask.current} is on. Pro adds as many as you like.`;
    case 'server': return 'Connecting your own server, like Amazon S3, Azure or a home NAS, is part of Pro.';
    case 'organize': return 'Organizing by place and date is part of Pro. Choosing photos yourself in Library and moving them stays free.';
    case 'trips': return 'Trip folders on your computer are part of Pro. Seeing your trips stays free.';
    default: return undefined;
  }
}

/** Whether removing `count` more would go past what the free plan has left today. Pro, or a plan not known yet, has no limit here: the service holds to it whatever the page says. */
export function overLimit(plan: PlanDto | undefined, count: number): boolean {
  return plan?.leftToday != null && count > plan.leftToday;
}

/** "Free plan: 38 removals left today.", for under a button that removes; none on Pro. */
export function limitLine(plan: PlanDto | undefined): string | undefined {
  if (plan?.leftToday == null) return undefined;
  return `Free plan: ${plan.leftToday.toLocaleString()} removal${plan.leftToday === 1 ? '' : 's'} left today.`;
}

/** "Nov 17, 2027". */
export const planDate = (iso: string) => new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });

/** "Nov 17", within the year. */
export const planDay = (iso: string) => new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric' });

/** Whole days from `now` until `iso`, as the design counts them (5 left on the third day of seven), and 1 on the last day while any time is left. */
export function daysLeft(iso: string, now: Date): number {
  const ms = new Date(iso).getTime() - now.getTime();
  return ms > 0 ? Math.max(1, Math.floor(ms / 86_400_000)) : 0;
}

/** The plan's title: "Trovolve Free", "Trovolve Pro trial", "Trovolve Pro" or "Trovolve Family". */
export function planTitle(plan: PlanDto): string {
  if (plan.tier === 'family') return 'Trovolve Family';
  if (plan.tier === 'trial') return 'Trovolve Pro trial';
  return plan.tier === 'pro' ? 'Trovolve Pro' : 'Trovolve Free';
}

/** The price of a plan as the store gives it, if it does. */
export const priceOf = (plan: PlanDto, kind: PlanKind) => plan.offers.find(o => o.kind === kind)?.price;

/**
 * The line under the plan's title in Settings: removals left on Free; days left and what follows on a trial; the plan
 * and when it renews on Pro or Family; and, on a computer, the phone it came from.
 */
export function planLine(plan: PlanDto, now: Date): string {
  if (plan.tier === 'free') return `${(plan.leftToday ?? 0).toLocaleString()} of ${plan.removalsPerDay.toLocaleString()} free removals left today`;
  const parts: string[] = [];
  if (plan.tier === 'trial') {
    if (plan.until) {
      const days = daysLeft(plan.until, now);
      parts.push(`${days} ${days === 1 ? 'day' : 'days'} left`);
    }
    const yearly = priceOf(plan, 'yearly');
    if (yearly) parts.push(`then ${yearly} a year`);
  } else {
    parts.push(plan.tier === 'family' ? 'Up to 6 people' : `${planName(plan.kind ?? 'yearly')} plan`);
    if (plan.until) parts.push(`renews ${planDate(plan.until)}`);
  }
  if (plan.sharedBy) parts.push(`from ${plan.sharedBy}`);
  return parts.join(' · ');
}

/** The `count` largest of `files`, in their own order; all of them when there are no more than that. */
export function largest<T extends { readonly sizeBytes: number }>(files: readonly T[], count: number): T[] {
  if (count >= files.length) return [...files];
  const keep = new Set([...files].sort((a, b) => b.sizeBytes - a.sizeBytes).slice(0, count));
  return files.filter(f => keep.has(f));
}

/** A store named in a sentence: "the App Store", or "Google Play", which takes no "the". */
export const theStore = (store: string) => (store === 'App Store' ? 'the App Store' : store);
