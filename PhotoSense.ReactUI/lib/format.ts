import type { DuplicateGroupDto, GroupMode, MatchKind, PhotoDto } from '../types';

export function formatBytes(bytes: number): string {
  if (bytes >= 1024 ** 3) return `${(bytes / 1024 ** 3).toFixed(2)} GB`;
  if (bytes >= 1024 ** 2) return `${(bytes / 1024 ** 2).toFixed(1)} MB`;
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

/** Capture time is the camera's wall-clock time; it is shown as recorded, not shifted to this computer's zone. */
export function formatTaken(takenOn?: string): string {
  if (!takenOn) return 'No capture date';
  const d = new Date(takenOn);
  return Number.isNaN(d.getTime()) ? takenOn : d.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'medium' });
}

export function formatDimensions(p: PhotoDto): string {
  return p.width > 0 ? `${p.width} × ${p.height}` : 'Size unknown';
}

export function formatDuration(seconds: number): string {
  const total = Math.round(seconds);
  const h = Math.floor(total / 3600), m = Math.floor((total % 3600) / 60), s = total % 60;
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}` : `${m}:${String(s).padStart(2, '0')}`;
}

/** Dimensions, format, playing time for a video, and file size, on one line. */
export function formatFile(p: PhotoDto): string {
  return [formatDimensions(p), p.format ?? '?', p.durationSeconds != null ? formatDuration(p.durationSeconds) : undefined, formatBytes(p.fileSizeBytes)]
    .filter(Boolean).join(' · ');
}

/** Where it was taken: the place name when one is near, otherwise the coordinates. */
export function formatPlace(p: PhotoDto): string {
  return p.placeName ?? formatCoordinates(p);
}

/** "and 2 linked files", for the sidecars and Live Photo videos that went with what was removed. */
export function linkedFiles(count: number): string {
  return count > 0 ? ` and ${count} linked ${count === 1 ? 'file' : 'files'}` : '';
}

export function hasCoordinates(p: PhotoDto): p is PhotoDto & { latitude: number; longitude: number } {
  return typeof p.latitude === 'number' && typeof p.longitude === 'number';
}

export function formatCoordinates(p: PhotoDto): string {
  if (!hasCoordinates(p)) return 'No location in this file';
  const lat = `${Math.abs(p.latitude).toFixed(4)}° ${p.latitude >= 0 ? 'N' : 'S'}`;
  const lon = `${Math.abs(p.longitude).toFixed(4)}° ${p.longitude >= 0 ? 'E' : 'W'}`;
  return `${lat}, ${lon}`;
}

export function mapUrl(p: PhotoDto): string | undefined {
  if (!hasCoordinates(p)) return undefined;
  return `https://www.openstreetmap.org/?mlat=${p.latitude}&mlon=${p.longitude}#map=15/${p.latitude}/${p.longitude}`;
}

/** The last part of a path: the folder's own name. */
export function leaf(path: string): string {
  const parts = path.split(/[\\/]+/).filter(Boolean);
  return parts.length > 0 ? parts[parts.length - 1] : path;
}

/** Whether two paths as typed name one folder. Windows takes no notice of case in a path; other systems do. */
export function sameFolder(a: string, b: string): boolean {
  const plain = (path: string) => {
    const trimmed = path.trim().replace(/[\\/]+$/, '');
    return /^([a-z]:|\\\\)/i.test(trimmed) ? trimmed.toLowerCase() : trimmed;
  };
  return plain(a) !== '' && plain(a) === plain(b);
}

/** Pixel size, with the playing time for a video. */
function pixels(p: PhotoDto): string {
  return p.durationSeconds != null ? `${formatDimensions(p)} · ${formatDuration(p.durationSeconds)}` : formatDimensions(p);
}

export interface Difference { label: string; text: string; }

/** What sets a copy apart from the original it was matched with: one entry for each thing that differs. */
export function differences(original: PhotoDto, copy: PhotoDto): Difference[] {
  const found: Difference[] = [];
  if (copy.fileName !== original.fileName) found.push({ label: 'Name', text: `the original is ${original.fileName}` });
  const format = copy.format ?? '?', originalFormat = original.format ?? '?';
  if (format !== originalFormat) found.push({ label: 'Format', text: `${format}, the original is ${originalFormat}` });
  if (copy.width !== original.width || copy.height !== original.height) found.push({ label: 'Pixel size', text: `${formatDimensions(copy)}, the original is ${formatDimensions(original)}` });
  const size = formatBytes(copy.fileSizeBytes), originalSize = formatBytes(original.fileSizeBytes);
  if (size !== originalSize) found.push({ label: 'File size', text: `${size}, the original is ${originalSize}` });
  if (copy.folder !== original.folder) found.push({ label: 'Folder', text: `${leaf(copy.folder)}, the original is in ${leaf(original.folder)}` });
  if (copy.takenOn !== original.takenOn) found.push({ label: 'Taken', text: `${formatTaken(copy.takenOn)}, the original: ${formatTaken(original.takenOn)}` });
  return found;
}

export interface CompareRow {
  label: string;
  original: string;
  copy: string;
  differs: boolean;
  /** A path, shown in the monospaced face. */
  mono?: boolean;
  /** A link to a map of where the original was taken. */
  map?: string;
}

/** The original and a copy, detail by detail, with the details that differ first. */
export function compareRows(original: PhotoDto, copy: PhotoDto): CompareRow[] {
  const row = (label: string, of: (p: PhotoDto) => string, extra: Partial<CompareRow> = {}): CompareRow => {
    const a = of(original), b = of(copy);
    return { label, original: a, copy: b, differs: a !== b, ...extra };
  };
  const rows = [
    row('Name', p => p.fileName),
    row('Format', p => p.format ?? '?'),
    row('File size', p => formatBytes(p.fileSizeBytes)),
    row('Folder', p => p.folder, { mono: true }),
    row('Taken', p => formatTaken(p.takenOn)),
    row('Place', formatPlace, { map: mapUrl(original) }),
    row(original.isVideo ? 'Video' : 'Pixel size', pixels),
    row('Camera', p => p.cameraModel ?? 'Not recorded'),
  ];
  return [...rows.filter(r => r.differs), ...rows.filter(r => !r.differs)];
}

/** One line about a group: what there is to remove, or that all of it is being kept. */
export function groupSummary(group: DuplicateGroupDto, mode: GroupMode): { text: string; allKept: boolean } {
  const n = group.members.length;
  if (mode === 'similar') return { text: `${n} similar ${n === 1 ? 'shot' : 'shots'}`, allKept: false };
  const removable = n - group.members.filter(m => m.photo.kept).length;
  if (removable === 0) return { text: `${n} ${n === 1 ? 'copy' : 'copies'}, all marked keep`, allKept: true };
  return { text: `${removable} ${removable === 1 ? 'duplicate' : 'duplicates'} · ${formatBytes(group.reclaimableBytes)}`, allKept: false };
}

export const matchLabel: Record<MatchKind, string> = {
  identical: 'Identical file',
  samePicture: 'Same picture',
  similar: 'Similar',
};

/** What each kind of match means, for the comparison window. */
export const matchMeaning: Record<MatchKind, string> = {
  identical: 'Byte-for-byte the same file.',
  samePicture: 'The same shot converted, resized or re-compressed.',
  similar: 'A burst frame or an edited version. Never removed in bulk.',
};
