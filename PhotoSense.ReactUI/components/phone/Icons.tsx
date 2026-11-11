import React from 'react';

interface IconProps {
  /** The width and height, in pixels. */
  readonly size?: number;
  /** The weight of the line. */
  readonly stroke?: number;
}

function Line({ size = 24, stroke = 1.9, children }: IconProps & { readonly children: React.ReactNode }) {
  return (
    <svg aria-hidden width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={stroke} strokeLinecap="round" strokeLinejoin="round">
      {children}
    </svg>
  );
}

/** Library: a grid of four rounded squares. */
export const LibraryIcon = (p: IconProps) => (
  <Line {...p}><rect x="3.5" y="3.5" width="7" height="7" rx="1.6" /><rect x="13.5" y="3.5" width="7" height="7" rx="1.6" /><rect x="3.5" y="13.5" width="7" height="7" rx="1.6" /><rect x="13.5" y="13.5" width="7" height="7" rx="1.6" /></Line>
);

/** Clean up: two rounded squares, one over the other. */
export const CleanUpIcon = (p: IconProps) => (
  <Line {...p}><rect x="8.5" y="8.5" width="12" height="12" rx="2.5" /><path d="M15.5 8.5V6a2.5 2.5 0 0 0-2.5-2.5H6A2.5 2.5 0 0 0 3.5 6v7A2.5 2.5 0 0 0 6 15.5h2.5" /></Line>
);

/** Organize, and a folder anywhere: a folder. */
export const FolderIcon = (p: IconProps) => (
  <Line {...p}><path d="M3.5 7.5a2 2 0 0 1 2-2h4l2 2.5h7a2 2 0 0 1 2 2v7.5a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z" /></Line>
);

/** Putting files in an album or folder: a folder with a plus. */
export const AddToFolderIcon = (p: IconProps) => (
  <Line {...p}><path d="M3.5 7.5a2 2 0 0 1 2-2h4l2 2.5h7a2 2 0 0 1 2 2v7.5a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z" /><path d="M12 11.5v5M9.5 14h5" /></Line>
);

/** Send: a laptop with an arrow going up. */
export const SendIcon = (p: IconProps) => (
  <Line {...p}><path d="M5 15.5V7a1.5 1.5 0 0 1 1.5-1.5h11A1.5 1.5 0 0 1 19 7v8.5" /><path d="M2.5 18.5h19" /><path d="M12 13V8.5M9.8 10.7 12 8.5l2.2 2.2" /></Line>
);

/** Delete: a bin. */
export const TrashIcon = (p: IconProps) => (
  <Line {...p}><path d="M4.5 7h15M9.5 7V5a1.5 1.5 0 0 1 1.5-1.5h2A1.5 1.5 0 0 1 14.5 5v2M6.5 7l.8 11.6A2 2 0 0 0 9.3 20.5h5.4a2 2 0 0 0 2-1.9L17.5 7" /></Line>
);

/** Settings: a gear. */
export const GearIcon = (p: IconProps) => (
  <Line stroke={1.7} {...p}>
    <circle cx="12" cy="12" r="3" />
    <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" />
  </Line>
);

/** Back on an iPhone: a chevron. */
export const ChevronBackIcon = (p: IconProps) => <Line size={20} stroke={2.2} {...p}><path d="M15 5l-7 7 7 7" /></Line>;

/** Back on Android: an arrow. */
export const ArrowBackIcon = (p: IconProps) => <Line size={22} stroke={2} {...p}><path d="M20 12H5M11 6l-6 6 6 6" /></Line>;

/** A row that leads further in: a chevron pointing on. */
export const ChevronIcon = (p: IconProps) => <Line size={14} stroke={2.2} {...p}><path d="M9 5l7 7-7 7" /></Line>;

/** Close, or stop selecting: a cross. */
export const CloseIcon = (p: IconProps) => <Line size={22} stroke={2} {...p}><path d="M6 6l12 12M18 6 6 18" /></Line>;

/** Hiding or showing an iPad's sidebar: a window with a column. */
export const SidebarIcon = (p: IconProps) => <Line size={22} stroke={1.8} {...p}><rect x="3" y="4.5" width="18" height="15" rx="3" /><path d="M9 4.5v15" /></Line>;

/** The map. */
export const MapIcon = (p: IconProps) => <Line size={21} stroke={1.8} {...p}><path d="M9 4.5 3.5 6.5v13l5.5-2 6 2 5.5-2v-13l-5.5 2z" /><path d="M9 4.5v13M15 6.5v13" /></Line>;

/** The list. */
export const ListIcon = (p: IconProps) => (
  <Line size={21} stroke={1.8} {...p}><path d="M9 6.5h11M9 12h11M9 17.5h11" /><circle cx="4.5" cy="6.5" r=".8" /><circle cx="4.5" cy="12" r=".8" /><circle cx="4.5" cy="17.5" r=".8" /></Line>
);

/** A photo: a frame with hills and a sun, for where photos are missing. */
export const ImageIcon = (p: IconProps) => (
  <Line size={34} stroke={1.7} {...p}><rect x="3.5" y="3.5" width="17" height="17" rx="3" /><path d="m3.5 16 4.5-4.5 4 4 3-3 5.5 5.5" /><circle cx="15.5" cy="8.5" r="1.5" /></Line>
);

/** A tick, white on the accent, for what is selected. */
export const TickIcon = ({ size = 14 }: IconProps) => (
  <svg aria-hidden width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="#fff" strokeWidth={3} strokeLinecap="round" strokeLinejoin="round"><path d="M5 12.5l4.5 4.5L19 7.5" /></svg>
);

/** Options: three sliders. */
export const SlidersIcon = (p: IconProps) => (
  <Line size={18} stroke={1.9} {...p}><path d="M4 7h9M17 7h3M4 17h3M11 17h9" /><circle cx="15" cy="7" r="2" /><circle cx="9" cy="17" r="2" /></Line>
);

/** Something new: a plus. */
export const PlusIcon = (p: IconProps) => <Line size={20} stroke={2} {...p}><path d="M12 5v14M5 12h14" /></Line>;

/** Done: a tick in the colour around it. */
export const DoneIcon = (p: IconProps) => <Line size={28} stroke={2.4} {...p}><path d="M5 12.5l4.5 4.5L19 7.5" /></Line>;
