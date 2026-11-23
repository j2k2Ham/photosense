import qrcode from 'qrcode-generator';

/**
 * The modules of a QR code for `text`, row by row, true where dark: the smallest version that holds it, with medium
 * error correction (a code on a screen, read from a little way off, can lose part of itself to glare).
 */
export function qrModules(text: string): boolean[][] {
  const code = qrcode(0, 'M');
  code.addData(text);
  code.make();
  const size = code.getModuleCount();
  return Array.from({ length: size }, (_, row) => Array.from({ length: size }, (_, col) => code.isDark(row, col)));
}

/** An SVG path drawing every dark module as a square one unit across, for a view box as wide as the code. */
export function qrPath(modules: readonly (readonly boolean[])[]): string {
  return modules.flatMap((row, y) => row.flatMap((dark, x) => (dark ? [`M${x} ${y}h1v1h-1z`] : []))).join('');
}
