import React, { useEffect, useRef, useState } from 'react';
import { appIconUrl, useAbout, useAboutText } from '../lib/apiClient';
import { parse, type Block, type Inline, type Question } from '../lib/markdown';
import type { AboutDocument, AboutDto } from '../types';
import { WindowShell } from './WindowShell';

type Topic = 'about' | AboutDocument;

const TOPICS: { id: Topic; title: string }[] = [
  { id: 'about', title: 'PhotoSense' },
  { id: 'support', title: 'Support' },
  { id: 'privacy', title: 'Privacy policy' },
  { id: 'terms', title: 'Terms of use' },
  { id: 'notices', title: 'Acknowledgements' },
];

const PROGRAMS: Record<string, string> = { server: 'for Windows and Mac', iphone: 'for iPhone and iPad' };

const link = 'font-medium text-brand underline underline-offset-2 hover:no-underline';

/** A line of a document, with its links opening outside PhotoSense. */
function Words({ parts }: { readonly parts: Inline[] }) {
  return <>{parts.map((part, i) => {
    if (part.kind === 'strong') return <strong key={i} className="font-semibold text-t1">{part.text}</strong>;
    if (part.kind === 'code') return <code key={i} className="font-mono text-[0.92em] text-t1">{part.text}</code>;
    if (part.kind === 'link') return <a key={i} href={part.href} target="_blank" rel="noreferrer" className={`${link} [overflow-wrap:anywhere]`}>{part.text}</a>;
    return <React.Fragment key={i}>{part.text}</React.Fragment>;
  })}</>;
}

/** The plus on a question, which turns into a minus while its answer is open. */
function OpenMark() {
  return (
    <span aria-hidden className="relative flex h-7 w-7 shrink-0 items-center justify-center rounded-full border border-line bg-bg text-brand transition-colors group-open:border-brand group-open:bg-brand group-open:text-on-brand">
      <span className="absolute h-0.5 w-3 rounded-full bg-current" />
      <span className="absolute h-3 w-0.5 rounded-full bg-current transition-transform duration-200 group-open:rotate-90" />
    </span>
  );
}

/** Questions one under another, each answer folded away until its question is opened. */
function Questions({ items }: { readonly items: Question[] }) {
  return (
    <div className="flex flex-col divide-y divide-line overflow-hidden rounded-2xl border border-line bg-s1">
      {items.map((q, n) => (
        <details key={n} className="group">
          {/* The ring is drawn inside the row: the rounded box around the questions would cut one outside it. */}
          <summary className="flex cursor-pointer list-none items-center gap-4 px-4 py-3.5 text-[15.5px] font-semibold leading-snug text-t1 hover:bg-s2 focus-visible:outline-offset-[-2px] sm:px-5 [&::-webkit-details-marker]:hidden">
            <span className="min-w-0 flex-1">{q.question}</span>
            <OpenMark />
          </summary>
          <div className="px-4 pb-5 pt-1 sm:px-5"><Blocks blocks={q.answer} /></div>
        </details>
      ))}
    </div>
  );
}

function Blocks({ blocks }: { readonly blocks: Block[] }) {
  return (
    <div className="flex flex-col gap-4 text-[15px] leading-relaxed text-t2">
      {blocks.map((block, i) => {
        if (block.kind === 'heading') {
          if (block.level === 1) return null;
          const Heading = block.level === 2 ? 'h3' : 'h4';
          return <Heading key={i} className={`font-semibold text-t1 ${block.level === 2 ? 'mt-3 text-[18px]' : 'text-[16px]'}`}>{block.text}</Heading>;
        }
        if (block.kind === 'paragraph') return <p key={i}><Words parts={block.inline} /></p>;
        if (block.kind === 'questions') return <Questions key={i} items={block.items} />;
        const List = block.ordered ? 'ol' : 'ul';
        return <List key={i} className={`flex flex-col gap-2 pl-5 ${block.ordered ? 'list-decimal' : 'list-disc'}`}>{block.items.map((item, n) => <li key={n}><Words parts={item} /></li>)}</List>;
      })}
    </div>
  );
}

/** A document as the web site has it; its own title is the topic's, shown above it. */
function Markdown({ text }: { readonly text: string }) {
  return <Blocks blocks={parse(text)} />;
}

function Waiting({ error }: { readonly error?: Error }) {
  return error
    ? <p role="alert" className="text-[15px] text-rose-t">PhotoSense could not show this: {error.message}</p>
    : <p role="status" className="text-[15px] text-t3">Loading…</p>;
}

/** One document, asked for only when it is turned to. */
function DocumentTopic({ document }: { readonly document: AboutDocument }) {
  const { data, error } = useAboutText(document);
  if (data === undefined) return <Waiting error={error} />;
  if (document === 'notices') return <pre className="whitespace-pre-wrap break-words font-mono text-[12.5px] leading-relaxed text-t2">{data}</pre>;
  return <Markdown text={data} />;
}

function Overview({ about }: { readonly about: AboutDto }) {
  return (
    <div className="flex flex-col gap-5 text-[15px] text-t2">
      <div className="flex items-center gap-4">
        <img src={appIconUrl} alt="" className="h-16 w-16 rounded-2xl" />
        <div className="flex flex-col gap-1">
          <p className="text-[22px] font-semibold text-t1">{about.name}</p>
          <p>Version <span className="font-mono text-t1">{about.version}</span> {PROGRAMS[about.program] ?? ''}</p>
        </div>
      </div>
      <p>{about.name} works on this device. Your photos, and everything {about.name} learns about them, stay here.</p>
      <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-5 gap-y-2">
        <dt className="text-t3">Made by</dt><dd className="text-t1">{about.publisher}</dd>
        {about.supportEmail && <><dt className="text-t3">Support</dt><dd><a href={`mailto:${about.supportEmail}`} className={link}>{about.supportEmail}</a></dd></>}
        {about.website && <><dt className="text-t3">Website</dt><dd><a href={about.website} target="_blank" rel="noreferrer" className={`${link} [overflow-wrap:anywhere]`}>{about.website}</a></dd></>}
      </dl>
      <p className="text-[13.5px] text-t3">{about.copyright}. All rights reserved.</p>
    </div>
  );
}

/** About PhotoSense: its version and maker, how to get help, the privacy policy, the terms of use, and what others made that it uses. */
export function AboutWindow({ onClose }: { onClose(): void }) {
  const [topic, setTopic] = useState<Topic>('about');
  const about = useAbout();
  const page = useRef<HTMLElement>(null);
  const title = TOPICS.find(t => t.id === topic)!.title;

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);
  // A topic is read from its top, wherever the one before it was left.
  useEffect(() => { page.current!.scrollTop = 0; }, [topic]);

  return (
    <WindowShell label="About PhotoSense" columns="lg:grid-cols-[260px_minmax(0,1fr)]" onClose={onClose}
      header={<span className="whitespace-nowrap text-[18px] font-semibold">About PhotoSense</span>}>
      <nav aria-label="About" className="flex min-h-0 flex-col gap-1 overflow-y-auto border-line bg-s1 p-3 max-lg:border-b lg:border-r">
        {TOPICS.map(t => (
          <button key={t.id} type="button" aria-current={t.id === topic ? 'page' : undefined} onClick={() => setTopic(t.id)}
            className={`rounded-xl px-3.5 py-2.5 text-left text-[14px] font-semibold ${t.id === topic ? 'bg-sel' : 'hover:bg-s2'}`}>{t.title}</button>
        ))}
      </nav>
      {/* A long document scrolls on its own, so the keyboard must be able to reach it to scroll it. */}
      {/* eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex */}
      <article ref={page} tabIndex={0} aria-label={title} className="min-h-0 overflow-y-auto bg-bg px-5 py-6 lg:px-10 lg:py-9">
        <div className="mx-auto flex max-w-[760px] flex-col gap-6">
          <h2 className="text-[24px] font-semibold leading-tight tracking-[-0.01em] lg:text-[30px]">{title}</h2>
          {topic === 'about'
            ? (about.data ? <Overview about={about.data} /> : <Waiting error={about.error} />)
            : <DocumentTopic key={topic} document={topic} />}
        </div>
      </article>
    </WindowShell>
  );
}
