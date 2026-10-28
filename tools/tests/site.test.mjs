import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { cpSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { after, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import { fill, html, main, page } from '../site.mjs';

const repo = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const made = [];
after(() => { for (const dir of made) rmSync(dir, { recursive: true, force: true }); });

const product = { name: 'PhotoSense', publisher: 'Jamie Hamman', copyright: '© 2026 Jamie Hamman', supportEmail: 'help@photosense.example', governingLaw: 'the State of Example' };

/** A copy of the site's sources, with product.json as given. */
function fixture(details) {
  const root = mkdtempSync(join(tmpdir(), 'photosense-site-'));
  made.push(root);
  cpSync(join(repo, 'site', 'pages'), join(root, 'site', 'pages'), { recursive: true });
  mkdirSync(join(root, 'branding'));
  cpSync(join(repo, 'branding', 'app-icon.png'), join(root, 'branding', 'app-icon.png'));
  writeFileSync(join(root, 'product.json'), JSON.stringify(details));
  return root;
}

describe('the documents', () => {
  it('are filled in with product.json\'s details, saying what is not set yet', () => {
    const text = '{{name}} by {{publisher}}: {{supportEmail}}, {{governingLaw}}. {{name}}.';
    assert.equal(fill(text, product), 'PhotoSense by Jamie Hamman: help@photosense.example, the State of Example. PhotoSense.');
    assert.equal(fill(text, { ...product, supportEmail: '', governingLaw: undefined }),
      'PhotoSense by Jamie Hamman: (the support address is not set yet), (the place whose law applies is not set yet). PhotoSense.');
  });

  it('are written as HTML, every character of the text shown as it is', () => {
    assert.equal(html([
      { kind: 'heading', level: 2, text: 'Q & A' },
      { kind: 'paragraph', inline: [{ kind: 'text', text: 'Use <b>' }, { kind: 'strong', text: 'Undo' }, { kind: 'code', text: 'a"b' }, { kind: 'link', text: 'us', href: 'mailto:help@photosense.example' }] },
      { kind: 'list', ordered: false, items: [[{ kind: 'text', text: 'one' }]] },
      { kind: 'list', ordered: true, items: [[{ kind: 'text', text: 'first' }], [{ kind: 'text', text: 'second' }]] },
    ]), '<h2>Q &amp; A</h2>\n<p>Use &lt;b&gt;<strong>Undo</strong><code>a&quot;b</code><a href="mailto:help@photosense.example">us</a></p>\n<ul><li>one</li></ul>\n<ol><li>first</li><li>second</li></ol>');
  });

  it('each make a page that says which it is, with the way to the others, and marks a draft as one', () => {
    const terms = page({ title: 'Terms of Use', file: 'terms.html', body: '<p>Body</p>', product, draft: false });
    assert.match(terms, /<title>Terms of Use \| PhotoSense<\/title>/);
    assert.match(terms, /<a href="terms.html" aria-current="page">Terms of Use<\/a>/);
    assert.match(terms, /<a href="privacy.html">Privacy Policy<\/a>/);
    assert.match(terms, /<footer>© 2026 Jamie Hamman<\/footer>/);
    assert.doesNotMatch(terms, /Draft/);
    assert.match(page({ title: 'Support', file: 'support.html', body: '', product, draft: true }), /<p class="draft">Draft: not every detail/);
  });
});

describe('the site', () => {
  it('is written with the icon, a front page and each document, once every detail is set', () => {
    const root = fixture(product);
    const said = [];
    assert.equal(main([], { root, print: s => said.push(s), fail: () => {} }), 0);
    assert.deepEqual(said, [`Wrote the site to ${join(root, 'site', 'out')}`]);
    const out = name => readFileSync(join(root, 'site', 'out', name), 'utf8');
    assert.match(out('index.html'), /<h1>PhotoSense<\/h1>/);
    assert.match(out('privacy.html'), /<h1>Privacy Policy<\/h1>/);
    assert.match(out('privacy.html'), /<a href="mailto:help@photosense.example">help@photosense.example<\/a>/);
    assert.match(out('terms.html'), /governed by the laws of the State of Example/);
    assert.match(out('support.html'), /<h3>Where do the files I delete go\?<\/h3>/);
    assert.doesNotMatch(out('support.html') + out('terms.html') + out('privacy.html'), /\{\{|not set yet/);
    assert.deepEqual(readFileSync(join(root, 'site', 'out', 'icon.png')), readFileSync(join(repo, 'branding', 'app-icon.png')));
  });

  it('is refused while the support address or the governing law is not set, unless asked for as a draft', () => {
    const root = fixture({ ...product, supportEmail: ' ', governingLaw: undefined });
    const failed = [], said = [];
    assert.equal(main([], { root, print: s => said.push(s), fail: s => failed.push(s) }), 1);
    assert.deepEqual(failed, ['The site would go out with supportEmail and governingLaw not set in product.json. Set them, or run with --draft to see a draft.']);
    assert.equal(main(['--draft'], { root, print: s => said.push(s), fail: () => {} }), 0);
    assert.deepEqual(said, [`Wrote the site to ${join(root, 'site', 'out')} (a draft)`]);
    assert.match(readFileSync(join(root, 'site', 'out', 'support.html'), 'utf8'), /\(the support address is not set yet\)/);
  });

  it('writes to the console unless told otherwise, and runs as a program', t => {
    const said = t.mock.method(console, 'log', () => {});
    const failed = t.mock.method(console, 'error', () => {});
    const root = fixture(product);
    assert.equal(main([], { root }), 0);
    assert.equal(said.mock.calls.length, 1);
    assert.equal(main([], { root: fixture({ ...product, supportEmail: '' }) }), 1);
    assert.equal(failed.mock.calls.length, 1);
    const ran = execFileSync(process.execPath, [join(repo, 'tools', 'site.mjs'), '--draft'], { encoding: 'utf8' });
    assert.match(ran, /^Wrote the site to .*site[\\/]out/);
  });
});
