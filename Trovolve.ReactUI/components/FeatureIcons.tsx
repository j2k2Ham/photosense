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
  similar: <><rect x="6" y="5" width="12" height="14" rx="2.5" fill="currentColor" opacity="0.3" transform="rotate(-14 12 12)" /><rect x="6" y="5" width="12" height="14" rx="2.5" fill="currentColor" opacity="0.55" transform="rotate(7 12 12)" /><rect x="6" y="5" width="12" height="14" rx="2.5" fill="currentColor" /></>,
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
  screenshot: <><path d="M8.3 2.5H15.7A2.8 2.8 0 0 1 18.5 5.3V18.7A2.8 2.8 0 0 1 15.7 21.5H8.3A2.8 2.8 0 0 1 5.5 18.7V5.3A2.8 2.8 0 0 1 8.3 2.5Z" fill="currentColor" opacity="0.3" /><path d="M8.6 9.4V6.6H11.2M12.8 6.6H15.4V9.4M15.4 14.6V17.4H12.8M11.2 17.4H8.6V14.6" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" /></>,
  burst: <><path d="M4.5 5H10.5A2.5 2.5 0 0 1 13 7.5V16.5A2.5 2.5 0 0 1 10.5 19H4.5A2.5 2.5 0 0 1 2 16.5V7.5A2.5 2.5 0 0 1 4.5 5Z" fill="currentColor" opacity="0.3" /><path d="M9 5H15A2.5 2.5 0 0 1 17.5 7.5V16.5A2.5 2.5 0 0 1 15 19H9A2.5 2.5 0 0 1 6.5 16.5V7.5A2.5 2.5 0 0 1 9 5Z" fill="currentColor" opacity="0.6" /><path d="M13.5 5H19.5A2.5 2.5 0 0 1 22 7.5V16.5A2.5 2.5 0 0 1 19.5 19H13.5A2.5 2.5 0 0 1 11 16.5V7.5A2.5 2.5 0 0 1 13.5 5Z" fill="currentColor" /></>,
  blurry: <><path d="M2.5 12a9.5 9.5 0 1 0 19 0a9.5 9.5 0 1 0 -19 0Z" fill="currentColor" opacity="0.18" /><path d="M5 12a7 7 0 1 0 14 0a7 7 0 1 0 -14 0Z" fill="currentColor" opacity="0.38" /><path d="M7.6 12a4.4 4.4 0 1 0 8.8 0a4.4 4.4 0 1 0 -8.8 0Z" fill="currentColor" /></>,
  messaging: <><path d="M5.9 3.5H12.6A3.4 3.4 0 0 1 16 6.9V10.6A3.4 3.4 0 0 1 12.6 14H5.9A3.4 3.4 0 0 1 2.5 10.6V6.9A3.4 3.4 0 0 1 5.9 3.5Z" fill="currentColor" opacity="0.4" /><path d="M11.5 9H17.6A3.4 3.4 0 0 1 21 12.4V15.6A3.4 3.4 0 0 1 17.6 19H17.3L18.3 21.6L14.4 19H11.5A3.4 3.4 0 0 1 8.1 15.6V12.4A3.4 3.4 0 0 1 11.5 9Z" fill="currentColor" /></>,
  storage: <><path d="M5.7 7H18.3A3.2 3.2 0 0 1 21.5 10.2V13.8A3.2 3.2 0 0 1 18.3 17H5.7A3.2 3.2 0 0 1 2.5 13.8V10.2A3.2 3.2 0 0 1 5.7 7Z" fill="currentColor" opacity="0.3" /><path d="M6.3 9.1H12.3A1.7 1.7 0 0 1 14 10.8V13.2A1.7 1.7 0 0 1 12.3 14.9H6.3A1.7 1.7 0 0 1 4.6 13.2V10.8A1.7 1.7 0 0 1 6.3 9.1Z" fill="currentColor" /></>,
  cloud: <><path d="M6.2 13.4A3.6 3.6 0 0 1 5.8 6.2A4.8 4.8 0 0 1 14.6 5.2" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" opacity="0.45" /><path d="M8.2 20.5A4.3 4.3 0 0 1 7.6 11.95A6 6 0 0 1 19.15 11.1A4.7 4.7 0 0 1 17.9 20.5Z" fill="currentColor" /></>,
  phones: <><path d="M4 4H15A2 2 0 0 1 17 6V12.5A2 2 0 0 1 15 14.5H4A2 2 0 0 1 2 12.5V6A2 2 0 0 1 4 4ZM2.2 16H16.8A1.2 1.2 0 0 1 18 17.2V17.2A1.2 1.2 0 0 1 16.8 18.4H2.2A1.2 1.2 0 0 1 1 17.2V17.2A1.2 1.2 0 0 1 2.2 16Z" fill="currentColor" opacity="0.4" /><path d="M16 8H20A2 2 0 0 1 22 10V19.5A2 2 0 0 1 20 21.5H16A2 2 0 0 1 14 19.5V10A2 2 0 0 1 16 8ZM17.25 17.9H18.75A0.65 0.65 0 0 1 19.4 18.55V18.55A0.65 0.65 0 0 1 18.75 19.2H17.25A0.65 0.65 0 0 1 16.6 18.55V18.55A0.65 0.65 0 0 1 17.25 17.9Z" fill="currentColor" fillRule="evenodd" /></>,
  fix: <><path d="M5.4 9.5H18.6A2.4 2.4 0 0 1 21 11.9V18.6A2.4 2.4 0 0 1 18.6 21H5.4A2.4 2.4 0 0 1 3 18.6V11.9A2.4 2.4 0 0 1 5.4 9.5Z" fill="currentColor" opacity="0.35" /><path d="M5 4.5H19A2 2 0 0 1 21 6.5V6.5A2 2 0 0 1 19 8.5H5A2 2 0 0 1 3 6.5V6.5A2 2 0 0 1 5 4.5Z" fill="currentColor" /><path d="M7.5 3V6.4M16.5 3V6.4" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" /><path d="M8.2 15.1L10.9 17.8L15.9 12.7" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" /></>,
  year: <><path d="M18.3 2.3C18.83 4.54 19.36 5.07 21.6 5.6C19.36 6.13 18.83 6.66 18.3 8.9C17.77 6.66 17.24 6.13 15 5.6C17.24 5.07 17.77 4.54 18.3 2.3Z" fill="currentColor" opacity="0.5" /><path d="M16.9 16.2C17.25 17.7 17.6 18.05 19.1 18.4C17.6 18.75 17.25 19.1 16.9 20.6C16.55 19.1 16.2 18.75 14.7 18.4C16.2 18.05 16.55 17.7 16.9 16.2Z" fill="currentColor" opacity="0.35" /><path d="M10 4.5C11.11 10.19 12.21 11.29 17.9 12.4C12.21 13.51 11.11 14.61 10 20.3C8.89 14.61 7.79 13.51 2.1 12.4C7.79 11.29 8.89 10.19 10 4.5Z" fill="currentColor" /></>,
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
