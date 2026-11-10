import './globals.css';
import React from 'react';
import { DM_Mono, DM_Sans } from 'next/font/google';
import { appIconUrl } from '../lib/apiBase';
import { THEME_KEY } from '../lib/themeKey';

const sans = DM_Sans({ subsets: ['latin'], weight: ['400', '500', '600', '700'], variable: '--font-sans' });
const mono = DM_Mono({ subsets: ['latin'], weight: ['400', '500'], variable: '--font-mono' });

/** The page's title, description and icon, as a browser and a search engine are told them. */
export const metadata = {
  title: 'PhotoSense',
  description: 'Find duplicate photos and videos, keep the best copy of each, and check the rest before anything moves.',
  icons: { icon: appIconUrl },
};

// Runs before the first paint, so a page saved in the light theme never flashes dark.
const applySavedTheme = `try{if(localStorage.getItem('${THEME_KEY}')==='light')document.documentElement.classList.add('light')}catch(e){}`;

/** The frame of every page: the fonts, the theme chosen before the page is drawn, and the page inside. */
export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className={`${sans.variable} ${mono.variable}`} suppressHydrationWarning>
      <head><script dangerouslySetInnerHTML={{ __html: applySavedTheme }} /></head>
      <body className="font-sans">{children}</body>
    </html>
  );
}
