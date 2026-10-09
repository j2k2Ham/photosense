import React, { useEffect, useState } from 'react';
import { googleMapsKey } from '../../lib/googleMaps';
import type { MapLayout } from '../../lib/organize';
import { WindowShell } from '../WindowShell';
import { ClusterMap } from './ClusterMap';
import { GoogleFolderMap, type MapView } from './GoogleFolderMap';

interface Props {
  readonly map: MapLayout;
  readonly focus?: string;
  /** How places were gathered into folders, said under the map. */
  readonly note: string;
  onFocus(key?: string): void;
}

/** The window the map is opened large in. It closes on Esc as well as on its button and a press outside it. */
function MapWindow({ folders, children, onClose }: { readonly folders: number; readonly children: React.ReactNode; onClose(): void }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <WindowShell label="Map" columns="grid-cols-1" onClose={onClose} header={(
      <>
        <span className="whitespace-nowrap text-[18px] font-semibold">Map</span>
        <span className="truncate text-[14px] text-t3">{folders.toLocaleString()} suggested {folders === 1 ? 'folder' : 'folders'} · click a dot to see that folder&apos;s files</span>
        <span className="flex-1" />
      </>
    )}>
      <div className="min-h-0 min-w-0 bg-stage p-5">{children}</div>
    </WindowShell>
  );
}

/**
 * The map of the suggested folders: Google's, with streets and satellite pictures, when a key for it is
 * set and Google can be reached; otherwise a plain one drawn here, which needs nothing from outside.
 * A click on the map opens it large, in a window of its own, where it does all that it does small.
 */
export function FolderMap({ map, focus, note, onFocus }: Props) {
  const [unavailable, setUnavailable] = useState<string>();
  const [large, setLarge] = useState(false);
  const [view, setView] = useState<MapView>('roadmap');
  const key = googleMapsKey();
  const google = key !== undefined && unavailable === undefined;
  const enlarge = () => setLarge(true), close = () => setLarge(false);
  // A dot picked on the large map shows its folder's files, which are behind the window: the window is put away.
  const pick = (folder?: string) => { onFocus(folder); close(); };
  const why = unavailable === undefined
    ? 'This is a plain map. To see streets and satellite pictures, set a Google Maps key: the README says how.'
    : `${unavailable} This plain map is shown instead.`;

  return (
    <>
      {google ? (
        <div className="flex flex-col items-center gap-2.5 pb-2">
          <GoogleFolderMap mapsKey={key} dots={map.dots} focus={focus} view={view} onView={setView} onFocus={onFocus} onEnlarge={enlarge} onUnavailable={setUnavailable} />
          <p className="text-center text-[12.5px] text-t3">{note} Each dot is a suggested folder: hover for its name, click to see its files. Click the map itself to open it large. Switch between Map and Satellite at its top left, and zoom with the wheel or the buttons. The map comes from Google; your pictures are not sent to it.</p>
        </div>
      ) : <ClusterMap map={map} focus={focus} onFocus={onFocus} onEnlarge={enlarge} note={`${note} Larger places are labeled. Hover a dot for its name, click to see its files. Click the map itself to open it large. ${why}`} />}
      {large && (
        <MapWindow folders={map.dots.length} onClose={close}>
          {google
            ? <GoogleFolderMap large mapsKey={key} dots={map.dots} focus={focus} view={view} onView={setView} onFocus={pick} onUnavailable={setUnavailable} />
            : <ClusterMap large map={map} focus={focus} onFocus={pick} />}
        </MapWindow>
      )}
    </>
  );
}
