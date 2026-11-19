import { formatBytes } from './format';
import type { LocationDto, LocationKind, LocationProgressDto } from '../types';

/** What each kind of location is called. */
export const KIND_NAMES: Record<LocationKind, string> = {
  computer: 'Computer', googleDrive: 'Google Drive', oneDrive: 'OneDrive', dropbox: 'Dropbox', box: 'Box',
  amazonS3: 'Amazon S3', s3Compatible: 'S3-compatible', azureBlob: 'Azure Blob Storage', webDav: 'WebDAV', sftp: 'SFTP',
};

/** A cloud service: signed in to, and always listed. */
export const isCloud = (kind: LocationKind) => kind === 'googleDrive' || kind === 'oneDrive' || kind === 'dropbox' || kind === 'box';

/** A location Connect to server added, which the person names. */
export const isServer = (kind: LocationKind) => !isCloud(kind) && kind !== 'computer';

/** A cloud service's tile: its first letter on its own colour, as the design draws it. */
export const CLOUD_TILES: Partial<Record<LocationKind, { readonly letter: string; readonly hue: number }>> = {
  googleDrive: { letter: 'G', hue: 150 }, oneDrive: { letter: 'O', hue: 245 }, dropbox: { letter: 'D', hue: 280 }, box: { letter: 'B', hue: 35 },
};

/** Where each cloud service keeps PhotoSense's copies, and what PhotoSense may reach there, as signing in says. */
export const CLOUD_FOLDERS: Partial<Record<LocationKind, { readonly folder: string; readonly scope: string }>> = {
  googleDrive: { folder: 'My Drive/PhotoSense', scope: 'a PhotoSense folder in My Drive' },
  oneDrive: { folder: 'Apps/PhotoSense', scope: 'its own folder, Apps/PhotoSense' },
  dropbox: { folder: 'Apps/PhotoSense', scope: 'its own folder, Apps/PhotoSense' },
  box: { folder: 'PhotoSense', scope: 'a folder named PhotoSense' },
};

/** Amazon's regions offered, as the design lists them. */
export const REGIONS = ['us-east-1 (N. Virginia)', 'us-west-2 (Oregon)', 'eu-west-1 (Ireland)', 'eu-central-1 (Frankfurt)', 'ap-southeast-2 (Sydney)'];

/** A field Connect to server asks for. */
export interface ServerField {
  /** Its name, as the service knows it. */
  readonly key: string;
  readonly label: string;
  /** What the empty field shows. */
  readonly placeholder: string;
  /** Typed text, a password, one of a list, or one of a few side by side. */
  readonly type: 'text' | 'secret' | 'select' | 'segments';
  readonly options?: readonly string[];
  readonly required?: boolean;
}

/** A kind Connect to server adds: what it is called, one line of help, and its fields in groups. */
export interface ServerKind {
  readonly kind: LocationKind;
  /** Its short name, on the picker. */
  readonly label: string;
  readonly help: string;
  readonly groups: readonly (readonly [string, readonly ServerField[]])[];
}

const text = (key: string, label: string, placeholder: string, required = false): ServerField => ({ key, label, placeholder, type: 'text', required });
const secret = (key: string, label: string): ServerField => ({ key, label, placeholder: 'Required', type: 'secret', required: true });

/** The kinds Connect to server adds, with their fields, as the design has them. SMB is left out (Jamie, 2026-11-19). */
export const SERVER_KINDS: readonly ServerKind[] = [
  {
    kind: 'amazonS3', label: 'Amazon S3', help: 'Use an access key that only reaches this bucket. PhotoSense needs to list, read and write files in its folder.',
    groups: [
      ['Bucket', [{ key: 'region', label: 'Region', placeholder: '', type: 'select', options: REGIONS, required: true }, text('bucket', 'Bucket', 'photos-archive', true), text('prefix', 'Folder', 'PhotoSense/')]],
      ['Access key', [text('akid', 'Access key ID', 'Required', true), secret('secret', 'Secret key')]],
      ['Storage', [{ key: 'cls', label: 'Storage class', placeholder: '', type: 'select', options: ['Standard', 'Standard-IA', 'Glacier Instant Retrieval'] }]],
    ],
  },
  {
    kind: 's3Compatible', label: 'S3-compatible', help: 'Backblaze B2, Wasabi, Cloudflare R2, MinIO and other services that use the S3 API.',
    groups: [
      ['Server', [text('endpoint', 'Endpoint', 'https://s3.example.com', true), text('region', 'Region', 'Optional')]],
      ['Bucket', [text('bucket', 'Bucket', 'photos', true), text('prefix', 'Folder', 'PhotoSense/')]],
      ['Access key', [text('akid', 'Key ID', 'Required', true), secret('secret', 'Secret key')]],
    ],
  },
  {
    kind: 'azureBlob', label: 'Azure Blob', help: 'Use a SAS token limited to one container with read, write and list permissions, or the storage account key.',
    groups: [
      ['Container', [text('account', 'Storage account', 'mystorageaccount', true), text('container', 'Container', 'photos', true), text('prefix', 'Folder', 'PhotoSense/')]],
      ['Sign in with', [{ key: 'auth', label: 'Method', placeholder: '', type: 'segments', options: ['SAS token', 'Account key'] }, secret('key', 'Key')]],
      ['Storage', [{ key: 'tier', label: 'Access tier', placeholder: '', type: 'segments', options: ['Hot', 'Cool', 'Cold'] }]],
    ],
  },
  {
    kind: 'webDav', label: 'WebDAV', help: 'Nextcloud, ownCloud and most NAS drives offer WebDAV.',
    groups: [
      ['Server', [text('url', 'Server URL', 'https://cloud.example.com/dav', true)]],
      ['Account', [text('user', 'User name', 'Required', true), secret('pass', 'Password')]],
      ['Location', [text('folder', 'Folder', '/Photos/PhotoSense')]],
    ],
  },
  {
    kind: 'sftp', label: 'SFTP', help: 'Any computer or server you can reach over SSH.',
    groups: [
      ['Server', [text('host', 'Host', 'example.com', true), text('port', 'Port', '22')]],
      ['Account', [text('user', 'User name', 'Required', true), { key: 'auth', label: 'Sign in with', placeholder: '', type: 'segments', options: ['Password', 'SSH key'] }, secret('pass', 'Password')]],
      ['Location', [text('folder', 'Folder', '/home/alex/PhotoSense')]],
    ],
  },
];

/** The first of each field's choices, which a new location starts with. */
export function startingSettings(kind: ServerKind): Record<string, string> {
  return Object.fromEntries(kind.groups.flatMap(([, fields]) => fields).filter(f => f.options).map(f => [f.key, f.options![0]]));
}

/** The host a location added reaches, as its line says it. */
export function hostOf(location: Pick<LocationDto, 'settings'>): string | undefined {
  const s = location.settings;
  if (s.url) {
    try { return new URL(s.url).host; } catch { return s.url; }
  }
  return s.bucket ?? s.host ?? s.account;
}

/** What a location's row says under its name: the account, where it is, or why it cannot be turned on. */
export function statusLine(location: LocationDto): string {
  if (location.problem) return location.problem;
  if (location.kind === 'computer') return location.connected ? `Paired · ${location.settings.folder}` : 'Not paired yet';
  if (isCloud(location.kind)) {
    if (!location.available) return 'Not available in this version yet';
    return location.connected ? location.account ?? 'Signed in' : 'Not connected';
  }
  return `${KIND_NAMES[location.kind]} · ${hostOf(location)}`;
}

/** A key shown masked: its first and last four, or dots for a short one. */
export const masked = (value: string) => (value.length > 8 ? `${value.slice(0, 4)}…${value.slice(-4)}` : '••••');

/** How far a location has got: "84 of 380 · 1.2 GB of 4.0 GB", or why it stopped. */
export function progressLine(progress: LocationProgressDto): string {
  if (progress.problem) return progress.problem;
  if (progress.total === 0) return 'Nothing to send';
  return `${progress.done.toLocaleString()} of ${progress.total.toLocaleString()} · ${formatBytes(progress.bytes)} of ${formatBytes(progress.totalBytes)}`;
}

/** The setting each field of a location added is shown by, in its details, with its key masked. */
export function detailRows(location: LocationDto): (readonly [string, string])[] {
  const kind = SERVER_KINDS.find(k => k.kind === location.kind);
  return (kind?.groups.flatMap(([, fields]) => fields) ?? [])
    .filter(f => f.type !== 'secret' && location.settings[f.key])
    .map(f => [f.label, f.key === 'akid' ? masked(location.settings[f.key]) : location.settings[f.key]] as const);
}
