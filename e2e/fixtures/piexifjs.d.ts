// piexifjs comes without types of its own; these are the parts the photos made for the tests use.
declare module 'piexifjs' {
  const piexif: {
    ImageIFD: Record<string, number>;
    ExifIFD: Record<string, number>;
    GPSIFD: Record<string, number>;
    dump(exif: Record<string, Record<number, unknown>>): string;
    insert(exif: string, jpeg: string): string;
  };
  export default piexif;
}
