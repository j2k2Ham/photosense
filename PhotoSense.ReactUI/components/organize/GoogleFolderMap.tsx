import React, { useEffect, useRef, useState } from 'react';
import { googleMapsId, loadGoogleMaps, onGoogleMapsRefused, type GoogleMap, type GoogleMaps, type GoogleMarker } from '../../lib/googleMaps';
import type { MapDot } from '../../lib/organize';
import { Segmented } from '../ModeSwitch';
import { EnlargeIcon } from './shared';

/** Streets, or satellite pictures with the streets and names drawn over them. */
export type MapView = 'roadmap' | 'hybrid';

interface Props {
  readonly mapsKey: string;
  readonly dots: readonly MapDot[];
  readonly focus?: string;
  /** Which of the two the map shows: kept by whoever shows the map, so that the small map and the large one agree. */
  readonly view: MapView;
  /** Filling the window it is in, not a panel's width. */
  readonly large?: boolean;
  onView(view: MapView): void;
  onFocus(key?: string): void;
  /** Opens the map large: on a click of the map itself, or of the button in its corner. */
  onEnlarge?(): void;
  /** Google's map cannot be shown; why, in words. */
  onUnavailable(why: string): void;
}

/** How far in a map of a single folder is drawn: a town and what is around it. */
const ONE_PLACE_ZOOM = 11;

/** A folder's dot: as wide as the folder has files, darker and with a heavier edge when it is the one picked. */
function dotOf(size: number, on: boolean): HTMLElement {
  const dot = document.createElement('span'), edge = on ? 3 : 1.5;
  Object.assign(dot.style, {
    display: 'block', boxSizing: 'border-box', width: `${size + edge}px`, height: `${size + edge}px`, borderRadius: '50%', cursor: 'pointer',
    background: on ? '#00525f' : 'rgba(13, 143, 161, 0.85)', border: `${edge}px solid #ffffff`,
  });
  return dot;
}

/**
 * The suggested folders as dots on Google's map: streets or satellite pictures, to zoom and move about in.
 * A dot is as large as its folder has files; a click shows that folder's files alone.
 */
export function GoogleFolderMap({ mapsKey, dots, focus, view, large = false, onView, onFocus, onEnlarge, onUnavailable }: Props) {
  const holder = useRef<HTMLDivElement>(null);
  const [maps, setMaps] = useState<GoogleMaps>();
  const [map, setMap] = useState<GoogleMap>();
  // The folders the map was last drawn to take in: it is drawn to fit again only when they are others.
  const fitted = useRef('');
  const unavailable = useRef(onUnavailable);
  unavailable.current = onUnavailable;
  const focusOn = useRef(onFocus);
  focusOn.current = onFocus;
  const enlarge = useRef(onEnlarge);
  enlarge.current = onEnlarge;

  useEffect(() => {
    let wanted = true;
    loadGoogleMaps(mapsKey).then(
      library => { if (wanted) setMaps(library); },
      (e: Error) => { if (wanted) unavailable.current(e.message); });
    const stop = onGoogleMapsRefused(() => unavailable.current('Google did not accept the map key.'));
    return () => { wanted = false; stop(); };
  }, [mapsKey]);

  useEffect(() => {
    if (!maps) return;
    const made = new maps.Map(holder.current!, {
      mapId: googleMapsId(), center: { lat: 0, lng: 0 }, zoom: 2, mapTypeId: 'roadmap',
      // Zooming always; no street-level pictures, which are not what this is for. Google's own switch between map
      // and satellite does not show on a small map, so the page has one of its own. The small map is opened large
      // by a click, so nothing on it but the dots answers a click; the large one can also take the whole screen.
      mapTypeControl: false, zoomControl: true, fullscreenControl: large, clickableIcons: large, streetViewControl: false, gestureHandling: 'greedy',
    });
    // A click on a dot is the dot's own and does not reach the map.
    made.addListener('click', () => enlarge.current?.());
    setMap(made);
  }, [maps, large]);

  useEffect(() => { map?.setMapTypeId(view); }, [map, view]);

  useEffect(() => {
    if (!maps || !map) return undefined;
    const markers: GoogleMarker[] = dots.map(d => {
      const on = d.key === focus;
      // Centred on the place, rather than hanging from it as Google's pins do; clickable, which also lets the keyboard reach it.
      const marker = new maps.marker.AdvancedMarkerElement({
        map, position: { lat: d.latitude, lng: d.longitude }, title: d.tip, zIndex: on ? 2 : 1, content: dotOf(d.size, on), gmpClickable: true, anchorLeft: '-50%', anchorTop: '-50%',
      });
      marker.addEventListener('gmp-click', () => focusOn.current(on ? undefined : d.key));
      return marker;
    });

    const folders = dots.map(d => d.key).join('\n');
    if (folders !== fitted.current) {
      fitted.current = folders;
      if (dots.length === 1) {
        map.setCenter({ lat: dots[0].latitude, lng: dots[0].longitude });
        map.setZoom(ONE_PLACE_ZOOM);
      } else {
        const bounds = new maps.LatLngBounds();
        for (const d of dots) bounds.extend({ lat: d.latitude, lng: d.longitude });
        map.fitBounds(bounds, large ? 72 : 36);
      }
    }
    return () => { for (const marker of markers) marker.map = null; };
  }, [maps, map, dots, focus, large]);

  return (
    <div role="group" aria-label={large ? 'Large map of the suggested folders' : 'Map of the suggested folders'}
      className={`relative w-full overflow-hidden rounded-[14px] border border-line bg-stage ${large ? 'h-full' : 'h-[380px]'}`}>
      <div ref={holder} className="h-full w-full" />
      {map
        ? <div className="absolute left-2.5 top-2.5 rounded-full bg-pop shadow-pop"><Segmented<MapView> label="Map view" value={view} onChange={onView} options={[{ value: 'roadmap', name: 'Map' }, { value: 'hybrid', name: 'Satellite' }]} /></div>
        : <p role="status" className="absolute inset-0 flex items-center justify-center text-[13px] text-t3">Loading the map…</p>}
      {map && onEnlarge && (
        <button type="button" aria-label="Enlarge the map" title="Enlarge the map" onClick={onEnlarge}
          className="absolute right-2.5 top-2.5 flex h-8 w-8 items-center justify-center rounded-full border border-line bg-pop text-t1 shadow-pop hover:bg-s2"><EnlargeIcon /></button>
      )}
    </div>
  );
}
