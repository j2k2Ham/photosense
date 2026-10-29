import './globals.css';
import React from 'react';
import { DM_Mono, DM_Sans } from 'next/font/google';
import { THEME_KEY } from '../lib/themeKey';

const sans = DM_Sans({ subsets: ['latin'], weight: ['400', '500', '600', '700'], variable: '--font-sans' });
const mono = DM_Mono({ subsets: ['latin'], weight: ['400', '500'], variable: '--font-mono' });

export const metadata = { title: 'PhotoSense', description: 'Find duplicate photos and videos, keep the best copy of each, and check the rest before anything moves.' };

// Runs before the first paint, so a page saved in the light theme never flashes dark.
const applySavedTheme = `try{if(localStorage.getItem('${THEME_KEY}')==='light')document.documentElement.classList.add('light')}catch(e){}`;

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className={`${sans.variable} ${mono.variable}`} suppressHydrationWarning>
      <head><script dangerouslySetInnerHTML={{ __html: applySavedTheme }} /></head>
      <body className="font-sans">{children}</body>
    </html>
  );
}
