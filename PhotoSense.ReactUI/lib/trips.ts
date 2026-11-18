import type { DistanceUnit } from './distance';
import type { TownDto, TripDto } from '../types';

const SHORT_MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

// A day written "2026-07-18", as its parts.
const parts = (day: string) => day.split('-').map(Number) as [number, number, number];

/**
 * When a trip was, as the design writes it: "Aug 9 – 12, 2026", "Jul 30 – Aug 2, 2026", "Dec 30, 2025 – Jan 2, 2026",
 * or "Mar 1, 2026" for a day.
 */
export function tripDates(start: string, end: string): string {
  const [y1, m1, d1] = parts(start), [y2, m2, d2] = parts(end);
  const first = `${SHORT_MONTHS[m1 - 1]} ${d1}`;
  if (y1 !== y2) return `${first}, ${y1} – ${SHORT_MONTHS[m2 - 1]} ${d2}, ${y2}`;
  if (m1 !== m2) return `${first} – ${SHORT_MONTHS[m2 - 1]} ${d2}, ${y2}`;
  return d1 === d2 ? `${first}, ${y1}` : `${first} – ${d2}, ${y1}`;
}

/** A trip's photos and videos, each by its own name: "388 photos", "512 photos and 3 videos", "10 videos". */
export function tripCount(photos: number, videos: number): string {
  const said = (n: number, one: string) => `${n.toLocaleString()} ${one}${n === 1 ? '' : 's'}`;
  const pictures = photos - videos;
  if (videos === 0) return said(pictures, 'photo');
  return pictures === 0 ? said(videos, 'video') : `${said(pictures, 'photo')} and ${said(videos, 'video')}`;
}

/** A trip's line under its name: "Aug 9 – 12, 2026 · 388 photos". */
export const tripLine = (t: TripDto) => `${tripDates(t.start, t.end)} · ${tripCount(t.photos, t.videos)}`;

/** A town as people say it: "Bozeman, Montana", or the town alone when its region is not known. */
export const townName = (t: TownDto) => (t.region ? `${t.town}, ${t.region}` : t.town);

/** What Trips says of how they were found: "Grouped by place and date from photos taken away from home in Bozeman, Montana." */
export const homeNote = (home: TownDto) => `Grouped by place and date from photos taken away from home in ${townName(home)}.`;

/** What Trips says when there is none yet, with how far away is away in the distances chosen. */
export const noTripsLine = (unit: DistanceUnit) =>
  `No trips yet. A trip is at least 10 photos and videos taken more than ${unit === 'km' ? '50 km' : 'about 30 miles'} from home, with no more than two days between them.`;

/** A day of a trip, as its heading says: "Day 1 · Jul 18". */
export function dayTitle(index: number, day: string): string {
  const [, month, date] = parts(day);
  return `Day ${index + 1} · ${SHORT_MONTHS[month - 1]} ${date}`;
}

/**
 * What Make a folder says under it: where the folder goes when the computer has said where it makes them, and that
 * nothing changes on the phone.
 */
export function folderNote(trip: TripDto, computer: string, tripsFolder: string | null, device: string): string {
  const where = tripsFolder ? `${tripsFolder}/${trip.folder}` : `a folder named ${trip.folder} on ${computer}`;
  return `Puts this trip’s copies in ${where}. Nothing changes on this ${device}.`;
}

/** A pin of the travel map, in percent of its width and height. */
export interface TripPin {
  readonly id: string;
  readonly x: number;
  readonly y: number;
  readonly name: string;
  readonly photos: number;
  /** How the label under it lines up with it: from its left near the map's left edge, to its right near the right edge, and centred between. */
  readonly label: 'start' | 'middle' | 'end';
}

/** The travel map: a pin for each trip, and home. */
export interface TripMap {
  readonly pins: readonly TripPin[];
  readonly home: { readonly x: number; readonly y: number };
}

const EARTH_KM = 6371;

// So that a long name near an edge stays inside the map.
const anchor = (x: number): TripPin['label'] => (x < 25 ? 'start' : x > 75 ? 'end' : 'middle');

/**
 * Lays the trips and home out on a plain map: east to the right, north up, distances true at the middle latitude,
 * with a margin so that no pin sits on the edge. Positions are in percent.
 */
export function tripMap(trips: readonly TripDto[], home: TownDto): TripMap {
  const points = [home, ...trips];
  const lats = points.map(p => p.latitude), lons = points.map(p => p.longitude);
  const midLat = (Math.min(...lats) + Math.max(...lats)) / 2, shrink = Math.max(0.05, Math.cos(midLat * Math.PI / 180));
  const kmPerDegree = 2 * Math.PI * EARTH_KM / 360;
  const east = (lon: number) => lon * kmPerDegree * shrink, north = (lat: number) => lat * kmPerDegree;
  // At least 40 km across, so that a trip next door is not blown up to fill the map.
  const spanX = Math.max(40, east(Math.max(...lons)) - east(Math.min(...lons))), spanY = Math.max(40, north(Math.max(...lats)) - north(Math.min(...lats)));
  const margin = 0.16 * Math.max(spanX, spanY);
  const width = spanX + 2 * margin, height = spanY + 2 * margin;
  const left = (east(Math.min(...lons)) + east(Math.max(...lons))) / 2 - width / 2, top = (north(Math.min(...lats)) + north(Math.max(...lats))) / 2 + height / 2;
  const x = (p: { longitude: number }) => (east(p.longitude) - left) / width * 100, y = (p: { latitude: number }) => (top - north(p.latitude)) / height * 100;
  return {
    pins: trips.map(t => ({ id: t.id, x: x(t), y: y(t), name: t.name, photos: t.photos, label: anchor(x(t)) })),
    home: { x: x(home), y: y(home) },
  };
}
