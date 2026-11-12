// Writes the web site the App Store listing and the app point to (site/out): the support page, the privacy policy and
// the terms of use, from the same Markdown the About window shows (site/pages), with product.json's details filled in.
// node tools/site.mjs refuses while the support address or the governing law is not set; --draft writes a marked draft.
import { copyFileSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { parse } from '../PhotoSense.ReactUI/lib/markdown.ts';

export const PAGES = [
  { file: 'support.html', source: 'support.md', title: 'Support' },
  { file: 'privacy.html', source: 'privacy-policy.md', title: 'Privacy Policy' },
  { file: 'terms.html', source: 'terms-of-use.md', title: 'Terms of Use' },
];

const escape = text => text.replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);

/** As PhotoSense.Api's AboutDocuments.Fill does for the app. */
export function fill(text, product) {
  return text
    .replaceAll('{{name}}', product.name)
    .replaceAll('{{publisher}}', product.publisher)
    .replaceAll('{{supportEmail}}', product.supportEmail?.trim() || '(the support address is not set yet)')
    .replaceAll('{{governingLaw}}', product.governingLaw?.trim() || '(the place whose law applies is not set yet)');
}

const words = parts => parts.map(part => {
  if (part.kind === 'strong') return `<strong>${escape(part.text)}</strong>`;
  if (part.kind === 'code') return `<code>${escape(part.text)}</code>`;
  if (part.kind === 'link') return `<a href="${escape(part.href)}">${escape(part.text)}</a>`;
  return escape(part.text);
}).join('');

/** A document's blocks as HTML. */
export function html(blocks) {
  return blocks.map(block => {
    if (block.kind === 'heading') return `<h${block.level}>${escape(block.text)}</h${block.level}>`;
    if (block.kind === 'paragraph') return `<p>${words(block.inline)}</p>`;
    // Each answer folded away under its question, opened by the browser itself: the site has no script.
    if (block.kind === 'questions') return `<div class="questions">${block.items.map(q => `<details><summary>${escape(q.question)}</summary>\n${html(q.answer)}\n</details>`).join('')}</div>`;
    const list = block.ordered ? 'ol' : 'ul';
    return `<${list}>${block.items.map(item => `<li>${words(item)}</li>`).join('')}</${list}>`;
  }).join('\n');
}

const STYLE = `:root{--bg:#f5f7f9;--fg:#161b1f;--muted:#55606a;--line:#d9dfe4;--brand:#007283}
@media (prefers-color-scheme:dark){:root{--bg:#0c0f11;--fg:#eef2f4;--muted:#a3adb5;--line:#26303a;--brand:#3ebfc6}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:16px/1.6 system-ui,-apple-system,"Segoe UI",sans-serif}
header,main,footer{max-width:760px;margin:0 auto;padding:0 16px}header{display:flex;flex-wrap:wrap;align-items:center;gap:12px 20px;padding-top:24px;padding-bottom:16px;border-bottom:1px solid var(--line)}
header a.home{display:flex;align-items:center;gap:10px;font-weight:600;font-size:18px;color:var(--fg);text-decoration:none}header img{width:32px;height:32px;border-radius:8px}
nav{display:flex;flex-wrap:wrap;gap:16px}nav a{color:var(--muted)}nav a[aria-current]{color:var(--fg);font-weight:600}
a{color:var(--brand)}main{padding-top:8px;padding-bottom:40px}h1{font-size:32px;line-height:1.2}h2{margin-top:32px}code{font-size:.92em}
li{margin:4px 0}.questions{border:1px solid var(--line);border-radius:16px;overflow:hidden}.questions details+details{border-top:1px solid var(--line)}
.questions summary{display:flex;align-items:center;gap:16px;padding:14px 18px;font-weight:600;cursor:pointer;list-style:none}.questions summary::-webkit-details-marker{display:none}
.questions summary::after{content:"+";flex:none;display:flex;align-items:center;justify-content:center;width:28px;height:28px;margin-left:auto;border:1px solid var(--line);border-radius:50%;color:var(--brand);font-size:20px;line-height:1}
.questions details[open]>summary::after{content:"\\2212";background:var(--brand);border-color:var(--brand);color:var(--bg)}.questions summary:focus-visible{outline:2px solid var(--brand);outline-offset:-2px}
.questions details>:not(summary){margin:0 18px 14px}.draft{background:#fff4d6;color:#5c4400;border:1px solid #e6c46a;border-radius:8px;padding:8px 12px}
footer{padding-top:16px;padding-bottom:32px;border-top:1px solid var(--line);color:var(--muted);font-size:14px}`;

/** A whole page of the site, with the way to the others. */
export function page({ title, file, body, product, draft }) {
  const links = PAGES.map(p => `<a href="${p.file}"${p.file === file ? ' aria-current="page"' : ''}>${escape(p.title)}</a>`).join('');
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${escape(title)} | ${escape(product.name)}</title>
<link rel="icon" href="icon.png">
<style>${STYLE}</style>
</head>
<body>
<header><a class="home" href="index.html"><img src="icon.png" alt="">${escape(product.name)}</a><nav aria-label="${escape(product.name)}">${links}</nav></header>
<main>
${draft ? '<p class="draft">Draft: not every detail of these pages is set yet.</p>\n' : ''}${body}
</main>
<footer>${escape(product.copyright)}</footer>
</body>
</html>
`;
}

/** node tools/site.mjs [--draft] */
export function main(argv, { root, print = console.log, fail = console.error }) {
  const draft = argv.includes('--draft');
  const product = JSON.parse(readFileSync(join(root, 'product.json'), 'utf8'));
  const unset = ['supportEmail', 'governingLaw'].filter(key => !product[key]?.trim());
  if (unset.length && !draft) {
    fail(`The site would go out with ${unset.join(' and ')} not set in product.json. Set them, or run with --draft to see a draft.`);
    return 1;
  }
  const out = join(root, 'site', 'out');
  mkdirSync(out, { recursive: true });
  copyFileSync(join(root, 'branding', 'app-icon.png'), join(out, 'icon.png'));
  const index = `<h1>${escape(product.name)}</h1>\n<p>${escape(product.name)} finds the copies and similar shots among your photos and videos, keeps the best of each, and sorts the rest into folders by place and date, all on your own device.</p>\n`
    + `<ul>${PAGES.map(p => `<li><a href="${p.file}">${escape(p.title)}</a></li>`).join('')}</ul>`;
  writeFileSync(join(out, 'index.html'), page({ title: product.name, file: 'index.html', body: index, product, draft }));
  for (const p of PAGES) {
    const body = html(parse(fill(readFileSync(join(root, 'site', 'pages', p.source), 'utf8'), product)));
    writeFileSync(join(out, p.file), page({ title: p.title, file: p.file, body, product, draft }));
  }
  print(`Wrote the site to ${out}${draft ? ' (a draft)' : ''}`);
  return 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = main(process.argv.slice(2), { root: join(dirname(fileURLToPath(import.meta.url)), '..') });
}
