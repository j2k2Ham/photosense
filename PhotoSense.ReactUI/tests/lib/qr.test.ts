import { describe, expect, it } from 'vitest';
import { qrModules, qrPath } from '../../lib/qr';

describe('the QR code', () => {
  it('is square, with the three finder patterns in its corners', () => {
    const modules = qrModules('photosense:pair?v=1&id=computer-1&name=Studio%20iMac&hosts=192.168.1.20&port=47616&key=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA');
    const size = modules.length;
    expect(modules.every(row => row.length === size)).toBe(true);
    // A finder pattern: a dark ring seven modules across, around a light ring, around a dark square of three.
    const finder = (top: number, left: number) => [0, 1, 2, 3, 4, 5, 6].map(r => [0, 1, 2, 3, 4, 5, 6].map(c => modules[top + r][left + c] ? 1 : 0).join('')).join('/');
    const expected = '1111111/1000001/1011101/1011101/1011101/1000001/1111111';
    expect([finder(0, 0), finder(0, size - 7), finder(size - 7, 0)]).toEqual([expected, expected, expected]);
  });

  it('is drawn as a square for each dark module', () => {
    expect(qrPath([[true, false], [false, true]])).toBe('M0 0h1v1h-1zM1 1h1v1h-1z');
  });
});
