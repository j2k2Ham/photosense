import React from 'react';

/** The features that have an icon of their own. */
export type FeatureIconName = 'duplicates' | 'similar' | 'organize' | 'places' | 'timeline' | 'backup' | 'vault' | 'compress' | 'swipe' | 'search' | 'removed' | 'best'
  | 'video' | 'screenshot' | 'burst' | 'blurry' | 'messaging' | 'storage' | 'cloud' | 'phones' | 'fix' | 'year';

/** Each feature's name, as the app says it. */
export const FEATURE_NAMES: Record<FeatureIconName, string> = {
  duplicates: 'Duplicates',
  similar: 'Similar shots',
  organize: 'Organize',
  places: 'Places',
  timeline: 'Timeline',
  backup: 'Backup',
  vault: 'Hidden vault',
  compress: 'Compress',
  swipe: 'Swipe to clean',
  search: 'Search',
  removed: 'Removed',
  best: 'Best copy',
  video: 'Large videos',
  screenshot: 'Screenshots',
  burst: 'Bursts',
  blurry: 'Blurry',
  messaging: 'Messaging apps',
  storage: 'Free up space',
  cloud: 'Locations',
  phones: 'Phones',
  fix: 'Fix dates and places',
  year: 'Year in review',
};

// Each drawing on a 24-unit grid, in one color with lighter layers for depth, the way the Trovolve mark is built.
const drawings: Record<FeatureIconName, React.ReactNode> = {
  duplicates: <><rect x="3" y="3" width="12" height="14" rx="2.5" fill="currentColor" opacity="0.4" /><rect x="9" y="7" width="12" height="14" rx="2.5" fill="currentColor" /></>,
  similar: <><path d="M10.3 5H17.3A2.5 2.5 0 0 1 19.8 7.5V16.5A2.5 2.5 0 0 1 17.3 19H10.3A2.5 2.5 0 0 1 7.8 16.5V7.5A2.5 2.5 0 0 1 10.3 5Z" fill="currentColor" opacity="0.3" transform="rotate(-24 13.8 19)" /><path d="M10.3 5H17.3A2.5 2.5 0 0 1 19.8 7.5V16.5A2.5 2.5 0 0 1 17.3 19H10.3A2.5 2.5 0 0 1 7.8 16.5V7.5A2.5 2.5 0 0 1 10.3 5Z" fill="currentColor" opacity="0.55" transform="rotate(-12 13.8 19)" /><path d="M10.3 5H17.3A2.5 2.5 0 0 1 19.8 7.5V16.5A2.5 2.5 0 0 1 17.3 19H10.3A2.5 2.5 0 0 1 7.8 16.5V7.5A2.5 2.5 0 0 1 10.3 5Z" fill="currentColor" /></>,
  organize: <><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v1H3Z" fill="currentColor" opacity="0.45" /><rect x="3" y="9" width="18" height="11" rx="2.2" fill="currentColor" /></>,
  places: <><ellipse cx="12" cy="20.5" rx="5.5" ry="1.6" fill="currentColor" opacity="0.35" /><path d="M12 20.5C11.4 20.5 5 14.4 5 9.8a7 7 0 0 1 14 0C19 14.4 12.6 20.5 12 20.5ZM12 7.2a2.6 2.6 0 1 0 0 5.2a2.6 2.6 0 1 0 0-5.2Z" fill="currentColor" fillRule="evenodd" /></>,
  timeline: <><rect x="7.5" y="3.5" width="9" height="3.2" rx="1.6" fill="currentColor" opacity="0.3" /><rect x="4" y="8.3" width="16" height="3.2" rx="1.6" fill="currentColor" opacity="0.55" /><rect x="6" y="13.1" width="12" height="3.2" rx="1.6" fill="currentColor" opacity="0.8" /><rect x="9" y="17.9" width="6" height="3.2" rx="1.6" fill="currentColor" /></>,
  backup: <><rect x="3" y="14" width="18" height="7" rx="2.2" fill="currentColor" opacity="0.4" /><path d="M12 16V4.5M7.5 9L12 4.5L16.5 9" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" /></>,
  vault: <><path d="M8 10.5V8a4 4 0 0 1 8 0v2.5" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" opacity="0.5" /><path d="M7.5 10h9a2.5 2.5 0 0 1 2.5 2.5v6a2.5 2.5 0 0 1-2.5 2.5h-9a2.5 2.5 0 0 1-2.5-2.5v-6a2.5 2.5 0 0 1 2.5-2.5ZM12 13.2l1.8 2.3l-1.8 2.3l-1.8-2.3Z" fill="currentColor" fillRule="evenodd" /></>,
  compress: <><rect x="3.2" y="3.2" width="17.6" height="17.6" rx="3" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" strokeDasharray="3 2.6" opacity="0.55" /><rect x="8" y="8" width="8" height="8" rx="1.8" fill="currentColor" /></>,
  swipe: <><rect x="4" y="5" width="11" height="15" rx="2.5" fill="currentColor" opacity="0.35" transform="rotate(-12 9.5 12.5)" /><rect x="9" y="4" width="11" height="15" rx="2.5" fill="currentColor" transform="rotate(10 14.5 11.5)" /></>,
  search: <><circle cx="10.5" cy="10.5" r="5.2" fill="currentColor" opacity="0.3" /><circle cx="10.5" cy="10.5" r="6.2" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" /><path d="M15.2 15.2L20 20" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" /></>,
  removed: <><path d="M9 4.5L4.5 9L9 13.5" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" /><path d="M4.5 9H14a5.5 5.5 0 0 1 0 11H9.5" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" opacity="0.55" /></>,
  best: <><polygon points="7.5,4.5 16.5,4.5 20.5,9.5 3.5,9.5" fill="currentColor" stroke="currentColor" strokeWidth="1" strokeLinejoin="round" opacity="0.55" /><polygon points="3.5,11.2 20.5,11.2 12,20.5" fill="currentColor" stroke="currentColor" strokeWidth="1" strokeLinejoin="round" /></>,
  // Drawn for Trovolve in the same way, for the features the brand export left without one.
  video: <><path d="M17.6 10.4L21.5 8V16L17.6 13.6Z" fill="currentColor" opacity="0.45" /><path d="M5.1 6H13.9A2.6 2.6 0 0 1 16.5 8.6V15.4A2.6 2.6 0 0 1 13.9 18H5.1A2.6 2.6 0 0 1 2.5 15.4V8.6A2.6 2.6 0 0 1 5.1 6ZM8 9.2V14.8L12.6 12Z" fill="currentColor" fillRule="evenodd" /></>,
  screenshot: <><path d="M8.3 3H15.7A2.8 2.8 0 0 1 18.5 5.8V18.2A2.8 2.8 0 0 1 15.7 21H8.3A2.8 2.8 0 0 1 5.5 18.2V5.8A2.8 2.8 0 0 1 8.3 3Z" fill="currentColor" opacity="0.35" /><path d="M8.8 9.6V6.8H11.2M12.8 6.8H15.2V9.6M15.2 14.4V17.2H12.8M11.2 17.2H8.8V14.4" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" /></>,
  burst: <><path d="M5.5 5H12.5A2.5 2.5 0 0 1 15 7.5V16.5A2.5 2.5 0 0 1 12.5 19H5.5A2.5 2.5 0 0 1 3 16.5V7.5A2.5 2.5 0 0 1 5.5 5Z" fill="currentColor" opacity="0.3" /><path d="M8.5 5H15.5A2.5 2.5 0 0 1 18 7.5V16.5A2.5 2.5 0 0 1 15.5 19H8.5A2.5 2.5 0 0 1 6 16.5V7.5A2.5 2.5 0 0 1 8.5 5Z" fill="currentColor" opacity="0.6" /><path d="M11.5 5H18.5A2.5 2.5 0 0 1 21 7.5V16.5A2.5 2.5 0 0 1 18.5 19H11.5A2.5 2.5 0 0 1 9 16.5V7.5A2.5 2.5 0 0 1 11.5 5Z" fill="currentColor" /></>,
  blurry: <><path d="M8 3H15A5 5 0 0 1 20 8V16A5 5 0 0 1 15 21H8A5 5 0 0 1 3 16V8A5 5 0 0 1 8 3Z" fill="currentColor" opacity="0.3" /><path d="M8.7 4.8H15.1A3.8 3.8 0 0 1 18.9 8.6V16A3.8 3.8 0 0 1 15.1 19.8H8.7A3.8 3.8 0 0 1 4.9 16V8.6A3.8 3.8 0 0 1 8.7 4.8Z" fill="currentColor" opacity="0.55" /><path d="M10.1 6.8H17.1A2.5 2.5 0 0 1 19.6 9.3V18.3A2.5 2.5 0 0 1 17.1 20.8H10.1A2.5 2.5 0 0 1 7.6 18.3V9.3A2.5 2.5 0 0 1 10.1 6.8Z" fill="currentColor" /></>,
  messaging: <><path d="M6 3.5H13A3 3 0 0 1 16 6.5V11A3 3 0 0 1 13 14H6A3 3 0 0 1 3 11V6.5A3 3 0 0 1 6 3.5Z" fill="currentColor" opacity="0.4" /><path d="M11 9H18A3 3 0 0 1 21 12V16A3 3 0 0 1 18 19H17.3L18.3 21.6L14.4 19H11A3 3 0 0 1 8 16V12A3 3 0 0 1 11 9Z" fill="currentColor" /></>,
  storage: <><path d="M5.5 6H18.5A2.5 2.5 0 0 1 21 8.5V15.5A2.5 2.5 0 0 1 18.5 18H5.5A2.5 2.5 0 0 1 3 15.5V8.5A2.5 2.5 0 0 1 5.5 6Z" fill="currentColor" opacity="0.3" /><path d="M5.5 6H7.5A2.5 2.5 0 0 1 10 8.5V15.5A2.5 2.5 0 0 1 7.5 18H5.5A2.5 2.5 0 0 1 3 15.5V8.5A2.5 2.5 0 0 1 5.5 6Z" fill="currentColor" /><path d="M16 8.6C16.48 11.05 16.95 11.52 19.4 12C16.95 12.48 16.48 12.95 16 15.4C15.52 12.95 15.05 12.48 12.6 12C15.05 11.52 15.52 11.05 16 8.6Z" fill="currentColor" opacity="0.6" /></>,
  cloud: <><path d="M6.6 12.8A3.1 3.1 0 0 1 7 6.7A4.3 4.3 0 0 1 14.6 6.2" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" opacity="0.45" /><path d="M7.3 20A3.5 3.5 0 0 1 7 13.05A5.5 5.5 0 0 1 17.3 12.1A4 4 0 0 1 16.9 20Z" fill="currentColor" /></>,
  phones: <><path d="M6.5 4.5H13.5A2.2 2.2 0 0 1 15.7 6.7V11.3A2.2 2.2 0 0 1 13.5 13.5H6.5A2.2 2.2 0 0 1 4.3 11.3V6.7A2.2 2.2 0 0 1 6.5 4.5ZM4.2 15.2H15.6A1.2 1.2 0 0 1 16.8 16.4A1.2 1.2 0 0 1 15.6 17.6H4.2A1.2 1.2 0 0 1 3 16.4A1.2 1.2 0 0 1 4.2 15.2Z" fill="currentColor" opacity="0.4" /><path d="M16.2 8H18.8A2.2 2.2 0 0 1 21 10.2V18.8A2.2 2.2 0 0 1 18.8 21H16.2A2.2 2.2 0 0 1 14 18.8V10.2A2.2 2.2 0 0 1 16.2 8ZM16.85 17.6H18.15A0.65 0.65 0 0 1 18.8 18.25A0.65 0.65 0 0 1 18.15 18.9H16.85A0.65 0.65 0 0 1 16.2 18.25A0.65 0.65 0 0 1 16.85 17.6Z" fill="currentColor" fillRule="evenodd" /></>,
  fix: <><path d="M5.4 9.5H18.6A2.4 2.4 0 0 1 21 11.9V18.6A2.4 2.4 0 0 1 18.6 21H5.4A2.4 2.4 0 0 1 3 18.6V11.9A2.4 2.4 0 0 1 5.4 9.5Z" fill="currentColor" opacity="0.35" /><path d="M5 4.5H19A2 2 0 0 1 21 6.5A2 2 0 0 1 19 8.5H5A2 2 0 0 1 3 6.5A2 2 0 0 1 5 4.5Z" fill="currentColor" /><path d="M7.5 4V6.4M16.5 4V6.4" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" /><path d="M8.2 15.1L10.9 17.8L15.9 12.7" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" /></>,
  year: <><path d="M5.5 3H12.5A2.5 2.5 0 0 1 15 5.5V14.5A2.5 2.5 0 0 1 12.5 17H5.5A2.5 2.5 0 0 1 3 14.5V5.5A2.5 2.5 0 0 1 5.5 3Z" fill="currentColor" opacity="0.35" /><path d="M14.6 8.1C15.5 12.71 16.39 13.6 21 14.5C16.39 15.4 15.5 16.29 14.6 20.9C13.7 16.29 12.81 15.4 8.2 14.5C12.81 13.6 13.7 12.71 14.6 8.1Z" fill="currentColor" /><path d="M18.7 3.3C18.98 4.74 19.26 5.02 20.7 5.3C19.26 5.58 18.98 5.86 18.7 7.3C18.42 5.86 18.14 5.58 16.7 5.3C18.14 5.02 18.42 4.74 18.7 3.3Z" fill="currentColor" opacity="0.55" /></>,
};

interface FeatureIconProps {
  readonly name: FeatureIconName;
  /** The width and height, in pixels. */
  readonly size?: number;
  readonly className?: string;
}

/** A feature's icon, drawn in the text color, so it follows the theme (text-brand gives the brand teal). */
export function FeatureIcon({ name, size = 24, className }: FeatureIconProps) {
  return (
    <svg aria-hidden width={size} height={size} viewBox="0 0 24 24" fill="none" className={className}>
      {drawings[name]}
    </svg>
  );
}
