// The little Markdown the privacy policy, terms and support page are written in (site/pages), read into blocks that
// the About window draws and tools/site.mjs writes as the web site. Plain TypeScript, so Node can run it as it is.

export type Inline =
  | { kind: 'text'; text: string }
  | { kind: 'strong'; text: string }
  | { kind: 'code'; text: string }
  | { kind: 'link'; text: string; href: string };

export type Block =
  | { kind: 'heading'; level: 1 | 2 | 3; text: string }
  | { kind: 'paragraph'; inline: Inline[] }
  | { kind: 'list'; ordered: boolean; items: Inline[][] }
  | { kind: 'questions'; items: Question[] };

/** A question of the frequently asked ones, with its answer: what follows its heading up to the next one. */
export interface Question { question: string; answer: Block[]; }

// A link written out, a bare web address, an email address, a bold run or code.
const INLINE = /\[([^\]]+)\]\(([^)\s]+)\)|(https?:\/\/[^\s)]+[^\s).,;:])|([\w.+-]+@[\w-]+(?:\.[\w-]+)+)|\*\*([^*]+)\*\*|`([^`]+)`/g;

/** A line's text, with its links, web addresses, bold and code picked out. */
export function inline(text: string): Inline[] {
  const parts: Inline[] = [];
  let at = 0;
  for (const found of text.matchAll(INLINE)) {
    if (found.index > at) parts.push({ kind: 'text', text: text.slice(at, found.index) });
    const [whole, label, target, bare, email, strong, code] = found;
    if (label !== undefined) parts.push({ kind: 'link', text: label, href: target });
    else if (bare !== undefined) parts.push({ kind: 'link', text: bare, href: bare });
    else if (email !== undefined) parts.push({ kind: 'link', text: email, href: `mailto:${email}` });
    else if (strong !== undefined) parts.push({ kind: 'strong', text: strong });
    else parts.push({ kind: 'code', text: code });
    at = found.index + whole.length;
  }
  if (at < text.length) parts.push({ kind: 'text', text: text.slice(at) });
  return parts;
}

/**
 * Headings (#, ##, ###), paragraphs, and lists (- or 1.), each item one line; blank lines end a paragraph or list.
 * A ### heading that ends in a question mark is a question, answered by what follows it; questions in a row are one list.
 */
export function parse(markdown: string): Block[] {
  return asked(blocks(markdown));
}

function asked(flat: Block[]): Block[] {
  const out: Block[] = [];
  let questions: Question[] | undefined;
  for (const block of flat) {
    if (block.kind === 'heading' && block.level === 3 && block.text.endsWith('?')) {
      if (!questions) {
        questions = [];
        out.push({ kind: 'questions', items: questions });
      }
      questions.push({ question: block.text, answer: [] });
    } else if (block.kind === 'heading' || !questions) {
      questions = undefined;
      out.push(block);
    } else {
      questions.at(-1)!.answer.push(block);
    }
  }
  return out;
}

function blocks(markdown: string): Block[] {
  const blocks: Block[] = [];
  let paragraph: string[] = [];
  let list: { ordered: boolean; items: Inline[][] } | undefined;
  const close = () => {
    if (paragraph.length) blocks.push({ kind: 'paragraph', inline: inline(paragraph.join(' ')) });
    if (list) blocks.push({ kind: 'list', ...list });
    paragraph = [];
    list = undefined;
  };
  for (const raw of markdown.split(/\r?\n/)) {
    const line = raw.trim();
    const heading = /^(#{1,3})\s+(.*)$/.exec(line);
    const item = /^(?:(-)|\d+\.)\s+(.*)$/.exec(line);
    if (!line) close();
    else if (heading) { close(); blocks.push({ kind: 'heading', level: heading[1].length as 1 | 2 | 3, text: heading[2] }); }
    else if (item) {
      const ordered = item[1] === undefined;
      if (paragraph.length || (list && list.ordered !== ordered)) close();
      list ??= { ordered, items: [] };
      list.items.push(inline(item[2]));
    } else {
      if (list) close();
      paragraph.push(line);
    }
  }
  close();
  return blocks;
}
