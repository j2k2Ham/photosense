import './globals.css';
import React from 'react';
import { TopBar } from '../components/TopBar';
import { Footer } from '../components/Footer';

export const metadata = { title: 'PhotoSense', description: 'PhotoSense Duplicate & Near-Duplicate Photo Scanner' };

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className="dark">
      <body className="flex flex-col min-h-screen">
        <TopBar />
        <main className="flex-1 flex flex-col overflow-hidden">{children}</main>
        <Footer />
      </body>
    </html>
  );
}
