import React from 'react';

export function TopBar() {
  return (
    <header className="h-14 flex items-center px-6 bg-brand-600 shadow-md z-10">
      <h1 className="text-lg font-semibold tracking-wide text-white">PhotoSense</h1>
      <div className="ml-auto flex gap-2 items-center">
        <a className="text-sm text-white/80 hover:text-white" href="https://github.com/j2k2Ham/photosense" target="_blank" rel="noreferrer">GitHub</a>
      </div>
    </header>
  );
}
