import { createContext, useContext, useSyncExternalStore } from 'react';

/** Which phone the page is in: an iPhone or iPad, or an Android phone or tablet. */
export type Platform = 'ios' | 'android';

/** How much room there is: a phone's, or a tablet's from 768 CSS pixels wide (iPad Split View and a folded phone too). */
export type Form = 'phone' | 'tablet';

/** The four tabs, in their order. */
export type TabId = 'library' | 'cleanup' | 'organize' | 'send';

/** What stands at a phone's foot: the tab bar, the bar of what can be done with a selection, or nothing (a pushed screen). */
export type Foot = 'tabs' | 'selection' | 'none';

/** The question a browser answers when the page is a tablet's width. */
export const TABLET = '(min-width: 768px)';

/** The phone a program runs on, or none on a computer. iPad reports `iphone` too. */
export function platformOf(program: string | undefined): Platform | undefined {
  if (program === 'iphone') return 'ios';
  if (program === 'android') return 'android';
  return undefined;
}

function watchWidth(changed: () => void) {
  const query = window.matchMedia(TABLET);
  query.addEventListener('change', changed);
  return () => query.removeEventListener('change', changed);
}
const tabletWide = () => window.matchMedia(TABLET).matches;
const phoneOnTheServer = () => false;

/** Phone or tablet, by the width the page has now, changing as it does (a tablet turned, or put beside another app). */
export function useForm(): Form {
  return useSyncExternalStore(watchWidth, tabletWide, phoneOnTheServer) ? 'tablet' : 'phone';
}

/** The words each platform has for the same things, as its own apps use them. */
export interface Words {
  /** A folder of photos: "album" on an iPhone, "folder" on Android. */
  readonly album: string;
  /** The same, capitalised to begin a sentence or a title. */
  readonly Album: string;
  /** More than one of them. */
  readonly albums: string;
  /** The whole library, in a title. */
  readonly library: string;
  /** What goes between the folders of a path in a sentence. */
  readonly separator: string;
  /** Where removed files wait. */
  readonly removed: string;
  /** The system's own bin. */
  readonly bin: string;
  /** What the selection bar offers for putting files somewhere. */
  readonly put: string;
}

/** Each platform's words. */
export const WORDS: Record<Platform, Words> = {
  ios: { album: 'album', Album: 'Album', albums: 'albums', library: 'Photo Library', separator: ' › ', removed: 'PhotoSense Removed', bin: 'Recently Deleted', put: 'Add to album' },
  android: { album: 'folder', Album: 'Folder', albums: 'folders', library: 'Phone', separator: '/', removed: 'Pictures/PhotoSense Removed', bin: 'the bin', put: 'Move to folder' },
};

/** Where the page is: the platform, the form, and their words; and how the shell around the screen stands. */
export interface PhoneFrame {
  readonly platform: Platform;
  readonly form: Form;
  readonly ios: boolean;
  readonly tablet: boolean;
  readonly words: Words;
  /** On an iPad with its sidebar hidden, a floating bar of tabs takes the top, so titles start lower. */
  readonly sidebarHidden: boolean;
  /** What stands at the foot of a phone's screen, which the screen leaves room for. */
  readonly foot: Foot;
  /** Opens Settings, from the gear of any screen. */
  openSettings(): void;
}

/** What the person is holding, as the page names it: "phone", "iPad", or an Android "tablet". */
export function deviceName(platform: Platform, tablet: boolean): string {
  return !tablet ? 'phone' : platform === 'ios' ? 'iPad' : 'tablet';
}

/** The frame a phone's screens are drawn in, given by the shell. */
export const PhoneContext = createContext<PhoneFrame | null>(null);

/** The frame a phone's screens are drawn in. Only the phone layout's own parts ask for it. */
export function usePhone(): PhoneFrame {
  const frame = useContext(PhoneContext);
  if (!frame) throw new Error('A phone screen is drawn only inside the phone layout.');
  return frame;
}
