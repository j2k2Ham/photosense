import { describe, expect, it } from 'vitest';
import { inline, parse } from '../../lib/markdown';

describe('a line of the documents', () => {
  it('picks out links, web addresses written out, bold and code, and keeps the text between them', () => {
    expect(inline('See [the policy](https://a.example/p), **always**, or `_PhotoSense_Removed` at https://b.example/x. Done')).toEqual([
      { kind: 'text', text: 'See ' },
      { kind: 'link', text: 'the policy', href: 'https://a.example/p' },
      { kind: 'text', text: ', ' },
      { kind: 'strong', text: 'always' },
      { kind: 'text', text: ', or ' },
      { kind: 'code', text: '_PhotoSense_Removed' },
      { kind: 'text', text: ' at ' },
      { kind: 'link', text: 'https://b.example/x', href: 'https://b.example/x' },
      { kind: 'text', text: '. Done' },
    ]);
  });

  it('makes an email address something to write to', () => {
    expect(inline('Write to help@photo-sense.example.com.')).toEqual([
      { kind: 'text', text: 'Write to ' },
      { kind: 'link', text: 'help@photo-sense.example.com', href: 'mailto:help@photo-sense.example.com' },
      { kind: 'text', text: '.' },
    ]);
  });

  it('is only text when there is nothing to pick out, and nothing when it is empty', () => {
    expect(inline('Plain words.')).toEqual([{ kind: 'text', text: 'Plain words.' }]);
    expect(inline('**bold**')).toEqual([{ kind: 'strong', text: 'bold' }]);
    expect(inline('')).toEqual([]);
  });
});

describe('a document', () => {
  it('is read into headings, paragraphs whose lines run together, and lists', () => {
    const blocks = parse('# Title\r\n\r\nFirst line\nsecond line.\n\n## Part\n- one\n- **two**\n\n### Step\n1. first\n2. second\n');
    expect(blocks).toEqual([
      { kind: 'heading', level: 1, text: 'Title' },
      { kind: 'paragraph', inline: [{ kind: 'text', text: 'First line second line.' }] },
      { kind: 'heading', level: 2, text: 'Part' },
      { kind: 'list', ordered: false, items: [[{ kind: 'text', text: 'one' }], [{ kind: 'strong', text: 'two' }]] },
      { kind: 'heading', level: 3, text: 'Step' },
      { kind: 'list', ordered: true, items: [[{ kind: 'text', text: 'first' }], [{ kind: 'text', text: 'second' }]] },
    ]);
  });

  it('starts a new block where a list follows a paragraph, a paragraph follows a list, or the kind of list changes', () => {
    expect(parse('Before:\n- a\nAfter.\n1. b\n- c').map(b => b.kind === 'list' ? `${b.ordered ? 'numbered' : 'bullets'} ${b.items.length}` : b.kind)).toEqual(
      ['paragraph', 'bullets 1', 'paragraph', 'numbered 1', 'bullets 1']);
  });

  it('makes a run of headings put as questions one list of questions, each answered by what follows it up to the next heading', () => {
    const blocks = parse('## Questions\n\n### Where?\n\nIn a folder.\n\n- kept\n\n### When?\n\n### Why not\n\nPlain.\n\n### Again?\n\nYes.');
    const text = (t: string) => ({ kind: 'paragraph', inline: [{ kind: 'text', text: t }] });
    expect(blocks).toEqual([
      { kind: 'heading', level: 2, text: 'Questions' },
      { kind: 'questions', items: [
        { question: 'Where?', answer: [text('In a folder.'), { kind: 'list', ordered: false, items: [[{ kind: 'text', text: 'kept' }]] }] },
        { question: 'When?', answer: [] },
      ] },
      // A heading that is not a question ends the list, and the next question starts another.
      { kind: 'heading', level: 3, text: 'Why not' },
      text('Plain.'),
      { kind: 'questions', items: [{ question: 'Again?', answer: [text('Yes.')] }] },
    ]);
    // Only the smallest heading asks.
    expect(parse('## Why?\n\nBecause.').map(b => b.kind)).toEqual(['heading', 'paragraph']);
  });

  it('is nothing at all when it is empty', () => {
    expect(parse('\n\n')).toEqual([]);
  });
});
