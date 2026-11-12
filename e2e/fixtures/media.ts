// Small photos and videos made for each test, carrying what a phone writes into them: when and where each was taken,
// and which picture and video are one Live Photo. Nothing here is anyone's own photo.
import jpeg from 'jpeg-js';
import piexif from 'piexifjs';
import { deflateSync } from 'node:zlib';

export interface Place { readonly latitude: number; readonly longitude: number }

/** Places GeoNames knows by name, far enough apart to be suggested as folders of their own. */
export const PLACES = {
  butte: { latitude: 46.0038, longitude: -112.5348 },
  anaconda: { latitude: 46.1283, longitude: -112.9423 },
  missoula: { latitude: 46.8721, longitude: -113.994 },
} satisfies Record<string, Place>;

export interface PictureOptions {
  /** Which picture: pictures of different seeds look nothing alike. */
  readonly seed?: number;
  /** A small change to the same picture, as a second shot of one scene is: brighter by this much. */
  readonly brighter?: number;
  readonly width?: number;
  readonly height?: number;
  /** When it was taken, as the camera's clock said it. */
  readonly taken?: Date;
  readonly at?: Place;
  /** Half of a Live Photo, whose video names the same identifier. */
  readonly livePhotoId?: string;
  readonly quality?: number;
}

// A picture of bands and blocks drawn from its seed: a different seed draws a different picture, and brighter one
// the same picture brighter.
function pixels(width: number, height: number, seed: number, brighter: number): Buffer {
  const data = Buffer.alloc(width * height * 4);
  const random = (n: number) => { const x = Math.sin(n * 12.9898 + seed * 78.233) * 43758.5453; return x - Math.floor(x); };
  const blocks = Array.from({ length: 16 }, (_, i) => [random(i * 3) * 255, random(i * 3 + 1) * 255, random(i * 3 + 2) * 255]);
  for (let y = 0; y < height; y++)
    for (let x = 0; x < width; x++) {
      const block = blocks[Math.floor((x / width) * 4) + Math.floor((y / height) * 4) * 4];
      const at = (y * width + x) * 4;
      for (let c = 0; c < 3; c++) data[at + c] = Math.max(0, Math.min(255, Math.round(block[c] * 0.7 + ((x + y * c) % 64) + brighter)));
      data[at + 3] = 255;
    }
  return data;
}

const exifDate = (d: Date) => `${d.getFullYear()}:${String(d.getMonth() + 1).padStart(2, '0')}:${String(d.getDate()).padStart(2, '0')} `
  + `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}:${String(d.getSeconds()).padStart(2, '0')}`;

// Degrees as EXIF writes them: degrees, minutes and seconds, each a fraction.
const dms = (value: number): [number, number][] => {
  const degrees = Math.floor(Math.abs(value));
  const minutes = Math.floor((Math.abs(value) - degrees) * 60);
  const seconds = Math.round(((Math.abs(value) - degrees) * 60 - minutes) * 60 * 1000);
  return [[degrees, 1], [minutes, 1], [seconds, 1000]];
};

// "Apple iOS", a version, the byte order, then one directory entry: tag 0x0011 holding the Live Photo's identifier.
function appleMakerNote(id: string): string {
  const text = Buffer.from(id + '\0', 'ascii');
  const head = Buffer.from('Apple iOS\0\x00\x01MM', 'binary');
  const entry = Buffer.alloc(2 + 12 + 4);
  entry.writeUInt16BE(1, 0);
  entry.writeUInt16BE(0x0011, 2);
  entry.writeUInt16BE(2, 4);
  entry.writeUInt32BE(text.length, 6);
  entry.writeUInt32BE(head.length + entry.length, 10);
  entry.writeUInt32BE(0, 14);
  return Buffer.concat([head, entry, text]).toString('binary');
}

/** A JPEG as a phone's camera writes one: the picture, and when, where and with what it was taken. */
export function picture(options: PictureOptions = {}): Buffer {
  const { seed = 1, brighter = 0, width = 320, height = 240, taken = new Date(2022, 6, 4, 10, 0, 0), at, livePhotoId, quality = 90 } = options;
  const encoded = jpeg.encode({ data: pixels(width, height, seed, brighter), width, height }, quality).data;
  const zeroth: Record<number, unknown> = { [piexif.ImageIFD.Make]: 'Apple', [piexif.ImageIFD.Model]: 'iPhone 13 Pro' };
  const exif: Record<number, unknown> = { [piexif.ExifIFD.DateTimeOriginal]: exifDate(taken), [piexif.ExifIFD.DateTimeDigitized]: exifDate(taken) };
  if (livePhotoId) exif[piexif.ExifIFD.MakerNote] = appleMakerNote(livePhotoId);
  const gps: Record<number, unknown> = at ? {
    [piexif.GPSIFD.GPSLatitudeRef]: at.latitude < 0 ? 'S' : 'N', [piexif.GPSIFD.GPSLatitude]: dms(at.latitude),
    [piexif.GPSIFD.GPSLongitudeRef]: at.longitude < 0 ? 'W' : 'E', [piexif.GPSIFD.GPSLongitude]: dms(at.longitude),
  } : {};
  const withExif = piexif.insert(piexif.dump({ '0th': zeroth, Exif: exif, GPS: gps }), encoded.toString('binary'));
  return Buffer.from(withExif, 'binary');
}

/** The same picture as a PNG, with nothing recorded about it: what a screenshot or an edited export is like. */
export function png(options: Pick<PictureOptions, 'seed' | 'brighter' | 'width' | 'height'> = {}): Buffer {
  const { seed = 1, brighter = 0, width = 320, height = 240 } = options;
  const rgba = pixels(width, height, seed, brighter);
  const rows = Buffer.alloc((width * 3 + 1) * height);
  for (let y = 0; y < height; y++)
    for (let x = 0; x < width; x++) for (let c = 0; c < 3; c++) rows[y * (width * 3 + 1) + 1 + x * 3 + c] = rgba[(y * width + x) * 4 + c];
  const crcTable = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
  const crc = (bytes: Buffer) => { let c = 0xffffffff; for (const b of bytes) c = crcTable[(c ^ b) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const chunk = (kind: string, body: Buffer) => {
    const head = Buffer.alloc(4); head.writeUInt32BE(body.length);
    const tail = Buffer.alloc(4); tail.writeUInt32BE(crc(Buffer.concat([Buffer.from(kind, 'ascii'), body])));
    return Buffer.concat([head, Buffer.from(kind, 'ascii'), body, tail]);
  };
  const header = Buffer.alloc(13); header.writeUInt32BE(width, 0); header.writeUInt32BE(height, 4); header.set([8, 2, 0, 0, 0], 8);
  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', header), chunk('IDAT', deflateSync(rows)), chunk('IEND', Buffer.alloc(0))]);
}

export interface MovieOptions {
  readonly livePhotoId?: string;
  readonly taken?: Date;
  readonly at?: Place;
  readonly seconds?: number;
}

const u16 = (n: number) => { const b = Buffer.alloc(2); b.writeUInt16BE(n); return b; };
const u32 = (n: number) => { const b = Buffer.alloc(4); b.writeUInt32BE(n >>> 0); return b; };
const atom = (type: string | Buffer, body: Buffer) => Buffer.concat([u32(8 + body.length), typeof type === 'string' ? Buffer.from(type, 'ascii') : type, body]);
const iso6709 = (p: Place) => `${p.latitude >= 0 ? '+' : '-'}${Math.abs(p.latitude).toFixed(4).padStart(7, '0')}${p.longitude >= 0 ? '+' : '-'}${Math.abs(p.longitude).toFixed(4).padStart(8, '0')}/`;

/** A QuickTime movie with no picture in it, as PhotoSense reads one: its length and size, and the phone's metadata. */
export function movie(options: MovieOptions = {}): Buffer {
  const { livePhotoId, taken = new Date(2022, 6, 4, 10, 0, 0), at, seconds = 3 } = options;
  const timescale = 600;
  const matrix = Buffer.concat([u32(0x00010000), u32(0), u32(0), u32(0), u32(0x00010000), u32(0), u32(0), u32(0), u32(0x40000000)]);
  const mvhd = atom('mvhd', Buffer.concat([u32(0), u32(0), u32(0), u32(timescale), u32(seconds * timescale), u32(0x00010000), u16(0x0100), Buffer.alloc(10), matrix, Buffer.alloc(24), u32(2)]));
  const tkhd = atom('tkhd', Buffer.concat([u32(1), u32(0), u32(0), u32(1), u32(0), u32(seconds * timescale), Buffer.alloc(8), u16(0), u16(0), u16(0), u16(0), matrix, u32(1920 << 16), u32(1080 << 16)]));
  const offset = -taken.getTimezoneOffset();
  const zone = `${offset >= 0 ? '+' : '-'}${String(Math.floor(Math.abs(offset) / 60)).padStart(2, '0')}${String(Math.abs(offset) % 60).padStart(2, '0')}`;
  const local = `${taken.getFullYear()}-${String(taken.getMonth() + 1).padStart(2, '0')}-${String(taken.getDate()).padStart(2, '0')}T`
    + `${String(taken.getHours()).padStart(2, '0')}:${String(taken.getMinutes()).padStart(2, '0')}:${String(taken.getSeconds()).padStart(2, '0')}${zone}`;
  const entries: [string, string][] = [['com.apple.quicktime.creationdate', local], ['com.apple.quicktime.model', 'iPhone 13 Pro']];
  if (at) entries.push(['com.apple.quicktime.location.ISO6709', iso6709(at)]);
  if (livePhotoId) entries.push(['com.apple.quicktime.content.identifier', livePhotoId]);
  const hdlr = atom('hdlr', Buffer.concat([u32(0), u32(0), Buffer.from('mdta', 'ascii'), Buffer.alloc(12), Buffer.from([0])]));
  const keys = atom('keys', Buffer.concat([u32(0), u32(entries.length), ...entries.map(([key]) => Buffer.concat([u32(8 + key.length), Buffer.from('mdta', 'ascii'), Buffer.from(key, 'ascii')]))]));
  const ilst = atom('ilst', Buffer.concat(entries.map(([, value], i) => atom(u32(i + 1), atom('data', Buffer.concat([u32(1), u32(0), Buffer.from(value, 'utf8')]))))));
  const meta = atom('meta', Buffer.concat([hdlr, keys, ilst]));
  return Buffer.concat([atom('ftyp', Buffer.concat([Buffer.from('qt  ', 'ascii'), u32(0), Buffer.from('qt  ', 'ascii')])), atom('moov', Buffer.concat([mvhd, atom('trak', tkhd), meta]))]);
}

/** The edit file Photos writes beside a picture it has changed. */
export const sidecar = () => Buffer.from('<?xml version="1.0" encoding="UTF-8"?>\n<plist version="1.0"><dict><key>adjustmentFormatIdentifier</key><string>com.apple.photo</string></dict></plist>\n', 'utf8');
