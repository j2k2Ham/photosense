// Works out a PhotoSense program's version from version.json and the repository's history: the release it is
// heading for (the last one, raised as version.json says), and the number of commits so far as its build number.
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { parseArgs } from 'node:util';

const KINDS = ['major', 'minor', 'patch'];

function checkKind(kind, where) {
  if (!KINDS.includes(kind)) throw new Error(`version.json: "${where}" must be "major", "minor" or "patch", not ${JSON.stringify(kind)}.`);
}

/** version.json, checked: a mistake there should stop the build, not number it wrongly. */
export function readConfig(text) {
  const config = JSON.parse(text);
  if (typeof config.linked !== 'boolean') throw new Error('version.json: "linked" must be true or false.');
  checkKind(config.release, 'release');
  for (const [part, own] of Object.entries(config.parts ?? {})) checkKind(own.release, `parts.${part}.release`);
  return config;
}

/** "v1.2.0" is a release of every program while they are linked; "iphone-v1.2.0" is one program's own. */
export function parseTag(tag) {
  const found = /^(?:([a-z][a-z0-9]*)-)?v(\d+)\.(\d+)\.(\d+)$/.exec(tag);
  return found ? { part: found[1] ?? null, version: [Number(found[2]), Number(found[3]), Number(found[4])] } : null;
}

const compare = (a, b) => a[0] - b[0] || a[1] - b[1] || a[2] - b[2];
const highest = versions => versions.reduce((best, v) => (compare(v, best) > 0 ? v : best), [0, 0, 0]);

export function bump([major, minor, patch], kind) {
  if (kind === 'major') return [major + 1, 0, 0];
  if (kind === 'minor') return [major, minor + 1, 0];
  return [major, minor, patch + 1];
}

/**
 * A program's version. While the programs are linked (and for the libraries, which belong to all of them) every
 * release counts, so that linking them again carries on above the highest; unlinked, a program counts the shared
 * releases and its own.
 */
export function versionOf({ config, part, tags, headTags, commits, release }) {
  const own = part && !config.linked;
  if (own && !config.parts?.[part]) throw new Error(`version.json has no part named "${part}".`);
  const kind = own ? config.parts[part].release : config.release;
  const counts = tag => tag && (!own || tag.part === null || tag.part === part);
  // A commit that carries its release's tag is that release, built again.
  const here = headTags.map(parseTag).filter(tag => tag && (tag.part === null || (own && tag.part === part)));
  const version = here.length
    ? highest(here.map(tag => tag.version))
    : bump(highest(tags.map(parseTag).filter(counts).map(tag => tag.version)), kind);
  const text = version.join('.');
  return { version: text, build: commits, display: release || here.length ? text : `${text}.${commits}` };
}

const lines = output => (output ?? '').split('\n').map(line => line.trim()).filter(Boolean);

/** What the repository says: every tag, those on the commit being built, and how many commits lead to it. */
export function readRepository(run) {
  return {
    tags: lines(run(['tag', '--list'])),
    headTags: lines(run(['tag', '--points-at', 'HEAD'])),
    commits: Number(lines(run(['rev-list', '--count', 'HEAD']))[0] ?? 0),
  };
}

export function git(cwd) {
  return args => {
    try {
      return execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
    } catch {
      // Outside a clone (a copy of the source, say) there is no history: version 0 builds from nothing released.
      return null;
    }
  };
}

const FORMATS = {
  // Read by Directory.Build.targets, which splits it at the semicolons.
  msbuild: v => `${v.version};${v.build};${v.display}`,
  json: v => JSON.stringify(v),
  text: v => v.display,
};

/** The command: --part server|iphone|..., --release for the release itself, --format msbuild|json|text. */
export function main(argv, { root, read = path => readFileSync(path, 'utf8'), run = git(root), print = console.log, fail = console.error }) {
  try {
    const { values } = parseArgs({ args: argv, options: { part: { type: 'string' }, release: { type: 'boolean' }, format: { type: 'string' } } });
    const format = FORMATS[values.format ?? 'text'];
    if (!format) throw new Error(`--format must be msbuild, json or text, not "${values.format}".`);
    const config = readConfig(read(join(root, 'version.json')));
    print(format(versionOf({ config, part: values.part || null, release: values.release ?? false, ...readRepository(run) })));
    return 0;
  } catch (error) {
    fail(`PhotoSense's version: ${error.message}`);
    return 1;
  }
}
