import React from 'react';

/** The features that have an icon of their own. */
export type FeatureIconName = 'duplicates' | 'similar' | 'organize' | 'places' | 'timeline' | 'backup' | 'vault' | 'compress' | 'swipe' | 'search' | 'removed' | 'best';

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
