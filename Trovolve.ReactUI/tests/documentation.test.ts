import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

// Every export of the page says what it is for in TSDoc (CLAUDE.md), as the compiler makes sure of in the service.
const root = join(__dirname, '..');
const exported = /^export\s+(?:default\s+)?(?:async\s+)?(?:function|const|let|type|interface|class|enum)\s+(\w+)/;

function sources(folder: string): string[] {
  return readdirSync(join(root, folder), { withFileTypes: true }).flatMap(entry =>
    entry.isDirectory() ? sources(join(folder, entry.name)) : /\.tsx?$/.test(entry.name) ? [join(folder, entry.name)] : []);
}

// The exports with no /** … */ just above them, allowing for decorators and 'use client' between.
function undocumented(text: string): string[] {
  const lines = text.split(/\r?\n/);
  return lines.flatMap((line, i) => {
    const name = exported.exec(line)?.[1];
    if (!name) return [];
    let above = i - 1;
    while (above >= 0 && /^\s*(@|'use client';)/.test(lines[above])) above--;
    if (above >= 0 && lines[above].trimEnd().endsWith('*/')) {
      let start = above;
      while (start > 0 && !lines[start].includes('/*')) start--;
      if (lines[start].includes('/**')) return [];
    }
    return [name];
  });
}

describe('the documentation of the page', () => {
  it('finds an export with no TSDoc, and passes one with it', () => {
    expect(undocumented("/** A size. */\nexport const a = 1;\n// Not TSDoc.\nexport function b() {}\n/* Nor this. */\nexport type C = 1;\n/**\n * Several lines.\n */\nexport interface D {}"))
      .toEqual(['b', 'C']);
  });

  it('says what each export is for', () => {
    const missing = [...sources('app'), ...sources('components'), ...sources('lib'), 'types.ts']
      .flatMap(file => undocumented(readFileSync(join(root, file), 'utf8')).map(name => `${relative(root, join(root, file))}: ${name}`));
    expect(missing).toEqual([]);
  });
});
