// Google's map library, as far as this page uses it. It is fetched from Google when a map is first shown,
// and only when a key for it has been set: without one the page asks Google for nothing.

export interface LatLng { lat: number; lng: number; }
export interface GoogleBounds { extend(at: LatLng): void; }
export interface GoogleMap {
  fitBounds(bounds: GoogleBounds, padding?: number): void; setCenter(at: LatLng): void; setZoom(zoom: number): void; setMapTypeId(kind: string): void;
  /** A click on the map itself: not on a marker, and not the end of a drag. */
  addListener(event: 'click', handler: () => void): void;
}
export interface GoogleMarker { setMap(map: GoogleMap | null): void; addListener(event: 'click', handler: () => void): void; }
export interface GoogleMaps {
  Map: new (element: HTMLElement, options: Record<string, unknown>) => GoogleMap;
  Marker: new (options: Record<string, unknown>) => GoogleMarker;
  LatLngBounds: new () => GoogleBounds;
  SymbolPath: { CIRCLE: unknown };
}

interface MapsWindow { google?: { maps: GoogleMaps }; photosenseMapsReady?(): void; gm_authFailure?(): void; }

/** The key Google's maps are asked for with; none unless NEXT_PUBLIC_GOOGLE_MAPS_KEY was set when the UI was started. */
export const googleMapsKey = () => process.env.NEXT_PUBLIC_GOOGLE_MAPS_KEY || undefined;

let loading: Promise<GoogleMaps> | undefined;
let refused = false;
const watchers = new Set<() => void>();

/** Fetches the library, once however many maps ask for it. Fails when Google cannot be reached. */
export function loadGoogleMaps(key: string): Promise<GoogleMaps> {
  loading ??= new Promise<GoogleMaps>((resolve, reject) => {
    const page = window as unknown as MapsWindow;
    page.photosenseMapsReady = () => resolve(page.google!.maps);
    // Google says a key is no good only after the library has loaded and a map has been drawn with it.
    page.gm_authFailure = () => { refused = true; watchers.forEach(tell => tell()); };
    const script = document.createElement('script');
    script.src = `https://maps.googleapis.com/maps/api/js?key=${encodeURIComponent(key)}&v=weekly&loading=async&callback=photosenseMapsReady`;
    script.async = true;
    script.onerror = () => {
      // Tried again the next time a map is shown: the connection may be back by then.
      loading = undefined;
      script.remove();
      reject(new Error('Google Maps could not be reached.'));
    };
    document.head.append(script);
  });
  return loading;
}

/** Tells the caller when Google refuses the key, at once if it already has. Returns the way to stop being told. */
export function onGoogleMapsRefused(tell: () => void): () => void {
  if (refused) tell();
  watchers.add(tell);
  return () => { watchers.delete(tell); };
}
