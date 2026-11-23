import type { ComputerNamesDto, FileNaming } from '../types';

const SHORT_MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** "2000-01-01" as the design writes a day: "Jan 1, 2000". */
export function dayWritten(day: string): string {
  const [year, month, date] = day.split('-').map(Number);
  return `${SHORT_MONTHS[month - 1]} ${date}, ${year}`;
}

/** A count of photos and videos together, as Fix dates and places says it: "1 item", "374 items". */
export const items = (n: number) => `${n.toLocaleString()} ${n === 1 ? 'item' : 'items'}`;

/** Camera clock not set's line: "32 items dated Jan 1, 2000", by the date most of them have. */
export const datesLine = (count: number, date: string) => `${items(count)} dated ${dayWritten(date)}`;

/** Missing location's note: how near, and the place most get. */
export function placesNote(count: number, place: string | null, placeCount: number): string {
  const most = place ? ` ${placeCount === count ? (count === 1 ? 'It gets' : 'All of them get') : `${placeCount.toLocaleString()} of them get`} ${place}.` : '';
  return `${count === 1 ? 'It was' : 'Each one was'} taken within 30 minutes of a photo that has a location.${most}`;
}

/** File names on the computer's line: "6,941 files named like IMG_4821". */
export function namesLine(names: ComputerNamesDto): string {
  const like = names.now ? ` named like ${names.now.replace(/\.[^.]+$/, '')}` : '';
  return `${names.files.toLocaleString()} ${names.files === 1 ? 'file' : 'files'}${like}`;
}

/** How copies are named, as the computer's choice says it. */
export const NAMING: Readonly<Record<FileNaming, string>> = { asOnPhone: 'As on the phone', date: 'Date and time', datePlace: 'Date, time and place' };
