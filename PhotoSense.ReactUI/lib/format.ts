import type { MatchKind, PhotoDto } from '../types';

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

export const matchLabel: Record<MatchKind, string> = {
  identical: 'Identical file',
  samePicture: 'Same picture',
  similar: 'Similar',
};
