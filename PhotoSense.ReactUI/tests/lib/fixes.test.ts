import { describe, expect, it } from 'vitest';
import { datesLine, dayWritten, items, NAMING, namesLine, placesNote } from '../../lib/fixes';
import type { ComputerNamesDto } from '../../types';

const names = (more: Partial<ComputerNamesDto> = {}): ComputerNamesDto => ({
  computer: 'Office PC', files: 6941, style: 'asOnPhone', now: 'IMG_4821.HEIC', after: '2026-03-02 14.31.07.HEIC', stage: null, problem: null, renamed: null, ...more,
});

describe("Fix dates and places' words", () => {
  it('write a day and a count as the design does', () => {
    expect(dayWritten('2000-01-01')).toBe('Jan 1, 2000');
    expect([items(1), items(374)]).toEqual(['1 item', '374 items']);
    expect(datesLine(32, '2000-01-01')).toBe('32 items dated Jan 1, 2000');
  });

  it('say how a place is found, and the place most get', () => {
    expect(placesNote(342, 'Glacier National Park', 214)).toBe('Each one was taken within 30 minutes of a photo that has a location. 214 of them get Glacier National Park.');
    expect(placesNote(2, 'Butte', 2)).toBe('Each one was taken within 30 minutes of a photo that has a location. All of them get Butte.');
    expect(placesNote(1, 'Butte', 1)).toBe('It was taken within 30 minutes of a photo that has a location. It gets Butte.');
    expect(placesNote(4, null, 0)).toBe('Each one was taken within 30 minutes of a photo that has a location.');
  });

  it("say the computer's files with an example of their names, and each way of naming them", () => {
    expect(namesLine(names())).toBe('6,941 files named like IMG_4821');
    expect(namesLine(names({ files: 1, now: null }))).toBe('1 file');
    expect(NAMING).toEqual({ asOnPhone: 'As on the phone', date: 'Date and time', datePlace: 'Date, time and place' });
  });
});
