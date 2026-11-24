import type { AboutDto } from '../types';

/** Which program this is, as the support page and an email to support say it. */
const PROGRAMS: Record<string, string> = { server: 'for Windows and Mac', iphone: 'for iPhone and iPad', android: 'for Android' };

/**
 * The system the page is running on, read from the browser's own description of itself: "Windows", "macOS",
 * "iOS 18.1", "iPadOS", "Android 14", or "Linux"; undefined when it says nothing Trovolve knows.
 */
export function systemOf(userAgent: string): string | undefined {
  const ios = /(?:iPhone|CPU) OS (\d+)(?:_(\d+))?/.exec(userAgent);
  if (/iPad/.test(userAgent)) return 'iPadOS';
  if (ios) return `iOS ${ios[1]}${ios[2] ? `.${ios[2]}` : ''}`;
  const android = /Android (\d+(?:\.\d+)?)/.exec(userAgent);
  if (android) return `Android ${android[1]}`;
  if (/Windows/.test(userAgent)) return 'Windows';
  if (/Mac OS X|Macintosh/.test(userAgent)) return 'macOS';
  if (/Linux/.test(userAgent)) return 'Linux';
  return undefined;
}

/** What an email to support carries before the person writes anything: the version, the system, and the latest errors. */
export function supportDetails(about: Pick<AboutDto, 'name' | 'version' | 'program'> | undefined, userAgent: string, errors: readonly string[]): string {
  const system = systemOf(userAgent);
  const lines = [
    about ? `${about.name} ${about.version} ${PROGRAMS[about.program] ?? ''}`.trim() : 'Trovolve',
    `System: ${system ?? 'not known'}`,
  ];
  // Only the last three: enough to see what went wrong, and none of the person's photos.
  if (errors.length > 0) lines.push(`Latest errors:\n${errors.slice(-3).map(e => `- ${e}`).join('\n')}`);
  return lines.join('\n');
}

/** A mailto link that opens a message to support with a subject, the details, and a place to say what happened. */
export function supportMailto(email: string, details: string): string {
  const body = `What I did:\n\nWhat I expected:\n\nWhat happened instead:\n\n---\n${details}\n`;
  return `mailto:${email}?subject=${encodeURIComponent('Trovolve support')}&body=${encodeURIComponent(body)}`;
}
