import { describe, expect, it } from 'vitest';
import { dayTitle, folderNote, homeNote, noTripsLine, townName, tripCount, tripDates, tripLine, tripMap } from '../../lib/trips';
import type { TownDto, TripDto } from '../../types';
import { photo } from '../fixtures';

const town = (more: Partial<TownDto> = {}): TownDto => ({ town: 'Bozeman', region: 'Montana', country: 'US', latitude: 45.68, longitude: -111.04, days: 0, ...more });
const trip = (more: Partial<TripDto> = {}): TripDto => ({
  id: 't1', name: 'Glacier National Park', folder: '2026-07 Glacier National Park', start: '2026-07-18', end: '2026-07-21', photos: 512, videos: 0,
  latitude: 48.68, longitude: -113.8, cover: photo(), folderMade: false, ...more,
});

describe('Trips\' words', () => {
  it('write when a trip was as the design does, within a month, across months, across years, or on one day', () => {
    expect(tripDates('2026-08-09', '2026-08-12')).toBe('Aug 9 – 12, 2026');
    expect(tripDates('2026-07-30', '2026-08-02')).toBe('Jul 30 – Aug 2, 2026');
    expect(tripDates('2025-12-30', '2026-01-02')).toBe('Dec 30, 2025 – Jan 2, 2026');
    expect(tripDates('2026-03-01', '2026-03-01')).toBe('Mar 1, 2026');
  });

  it('say photos and videos each by their own name', () => {
    expect(tripCount(388, 0)).toBe('388 photos');
    expect(tripCount(1, 0)).toBe('1 photo');
    expect(tripCount(515, 3)).toBe('512 photos and 3 videos');
    expect(tripCount(2, 1)).toBe('1 photo and 1 video');
    expect(tripCount(10, 10)).toBe('10 videos');
    expect(tripLine(trip({ photos: 1388 }))).toBe('Jul 18 – 21, 2026 · 1,388 photos');
  });

  it('name home with its region, or alone when the region is not known, and say how trips were found', () => {
    expect(townName(town())).toBe('Bozeman, Montana');
    expect(townName(town({ region: '' }))).toBe('Bozeman');
    expect(homeNote(town())).toBe('Grouped by place and date from photos taken away from home in Bozeman, Montana.');
    expect(noTripsLine('miles')).toBe('No trips yet. A trip is at least 10 photos and videos taken more than about 30 miles from home, with no more than two days between them.');
    expect(noTripsLine('km')).toContain('more than 50 km from home');
    expect(dayTitle(0, '2026-07-18')).toBe('Day 1 · Jul 18');
  });

  it('say where the folder goes once the computer has said, and otherwise by its name', () => {
    expect(folderNote(trip(), 'Studio iMac', 'Pictures/PhotoSense', 'iPhone'))
      .toBe('Puts this trip’s copies in Pictures/PhotoSense/2026-07 Glacier National Park. Nothing changes on this iPhone.');
    expect(folderNote(trip(), 'Studio iMac', null, 'phone'))
      .toBe('Puts this trip’s copies in a folder named 2026-07 Glacier National Park on Studio iMac. Nothing changes on this phone.');
  });
});

describe('The travel map', () => {
  it('puts each trip and home where they are, north up and east right, labels kept inside near the edges', () => {
    const west = trip({ id: 'w', latitude: 45.68, longitude: -114 });
    const east = trip({ id: 'e', latitude: 45.68, longitude: -108 });
    const north = trip({ id: 'n', latitude: 48, longitude: -111.04 });
    const map = tripMap([west, east, north], town());
    const at = Object.fromEntries(map.pins.map(p => [p.id, p]));
    expect(at.w.x).toBeLessThan(map.home.x);
    expect(at.e.x).toBeGreaterThan(map.home.x);
    expect(at.n.y).toBeLessThan(map.home.y);
    expect([at.w.label, at.e.label, at.n.label]).toEqual(['start', 'end', 'middle']);
    for (const p of [...map.pins, map.home]) for (const v of [p.x, p.y]) expect(v > 0 && v < 100).toBe(true);
    expect(at.n).toMatchObject({ name: 'Glacier National Park', photos: 512 });
  });

  it('is never less than some 40 km across, so that a trip next door is not blown up to fill it', () => {
    const map = tripMap([trip({ latitude: 45.69, longitude: -111.04 })], town());
    expect(Math.abs(map.pins[0].y - map.home.y)).toBeLessThan(5);
  });
});
