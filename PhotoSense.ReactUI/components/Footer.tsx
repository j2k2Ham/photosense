import React from 'react';

export function Footer() {
  return (
    <footer className="text-xs text-neutral-500 px-6 py-3 border-t border-neutral-800 bg-neutral-900/60">
      <span>PhotoSense © {new Date().getFullYear()}</span>
    </footer>
  );
}
