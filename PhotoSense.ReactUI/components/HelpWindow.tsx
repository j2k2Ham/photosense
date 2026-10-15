import React, { useEffect, useMemo, useRef, useState } from 'react';
import { GUIDE, searchGuide, type GuidePlace } from '../lib/guide';
import { WindowShell } from './WindowShell';

interface Props {
  /** Takes the reader to the place a topic describes; the guide is put away for it. */
  onGo(place: GuidePlace): void;
  onClose(): void;
}

/** A line of the guide, with what it names on screen in bold. */
function rich(text: string) {
  return text.split('**').map((part, i) => (i % 2 === 1 ? <strong key={i} className="font-semibold text-t1">{part}</strong> : part));
}

/**
 * The user guide: its topics down the left, the one being read on the right. It can be searched, read
 * from one topic to the next, and each topic that describes a place in PhotoSense has a button that goes there.
 */
export function HelpWindow({ onGo, onClose }: Props) {
  const [query, setQuery] = useState('');
  const [at, setAt] = useState(GUIDE[0].id);
  const found = useMemo(() => searchGuide(query), [query]);
  // The topic being read, while the search still finds it; otherwise the first one it does find.
  const topic = found.find(t => t.id === at) ?? found[0];
  const index = found.indexOf(topic);
  const before = found[index - 1], after = found[index + 1];
  const page = useRef<HTMLElement>(null);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);
  // A topic is read from its top, wherever the one before it was left.
  useEffect(() => { page.current!.scrollTop = 0; }, [topic]);

  const step = 'flex min-w-0 max-w-[48%] flex-col gap-0.5 rounded-xl border border-line px-4 py-2.5 text-left hover:bg-s2';
  return (
    <WindowShell label="Help" columns="grid-cols-[300px_minmax(0,1fr)]" onClose={onClose} header={(
      <>
        <span className="whitespace-nowrap text-[18px] font-semibold">User guide</span>
        <label className="ml-3 flex h-[38px] w-[clamp(200px,24vw,340px)] items-center gap-2.5 rounded-full border border-line bg-bg px-3.5">
          <span aria-hidden className="h-[11px] w-[11px] shrink-0 rounded-full border-[1.5px] border-t3" />
          <input value={query} onChange={e => setQuery(e.target.value)} placeholder="Search the guide" aria-label="Search the guide" className="min-w-0 flex-1 bg-transparent text-[14px] outline-none placeholder:text-t3" />
        </label>
        <span className="flex-1" />
      </>
    )}>
      <nav aria-label="Topics" className="flex min-h-0 flex-col gap-1 overflow-y-auto border-r border-line bg-s1 p-3">
        {found.map(t => (
          <button key={t.id} type="button" aria-current={t === topic ? 'page' : undefined} onClick={() => setAt(t.id)}
            className={`flex flex-col gap-0.5 rounded-xl px-3.5 py-2.5 text-left ${t === topic ? 'bg-sel' : 'hover:bg-s2'}`}>
            <span className="text-[14px] font-semibold">{t.title}</span>
            <span className="text-[12.5px] text-t3">{t.summary}</span>
          </button>
        ))}
        {found.length === 0 && (
          <div className="flex flex-col items-start gap-2.5 px-3.5 py-3">
            <p className="text-[13.5px] text-t2">Nothing in the guide mentions “{query.trim()}”.</p>
            <button type="button" onClick={() => setQuery('')} className="pill-outline h-8 px-3.5 text-[13px] font-medium">Show every topic</button>
          </div>
        )}
      </nav>

      <article ref={page} aria-label={topic ? topic.title : 'Nothing found'} className="min-h-0 overflow-y-auto bg-bg px-10 py-9">
        {topic && (
          <div className="mx-auto flex max-w-[760px] flex-col gap-7">
            <header className="flex flex-col gap-3">
              <p className="label-caps">{index + 1} of {found.length}</p>
              <h2 className="text-[30px] font-semibold leading-tight tracking-[-0.01em]">{topic.title}</h2>
              <p className="text-[16px] text-t2">{topic.summary}</p>
              {topic.go && <button type="button" onClick={() => onGo(topic.go!.place)} className="pill-brand mt-1 h-10 self-start px-5 text-[14px]">{topic.go.label}</button>}
            </header>

            {topic.sections.map((section, i) => (
              <section key={i} className="flex flex-col gap-3 text-[15px] leading-relaxed text-t2">
                {section.heading && <h3 className="text-[18px] font-semibold text-t1">{section.heading}</h3>}
                {section.text?.map((paragraph, n) => <p key={n}>{rich(paragraph)}</p>)}
                {section.steps && (
                  <ol className="flex flex-col gap-2.5">
                    {section.steps.map((line, n) => (
                      <li key={n} className="flex gap-3.5">
                        <span aria-hidden className="mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-brand font-mono text-[12.5px] font-semibold text-on-brand">{n + 1}</span>
                        <span className="min-w-0">{rich(line)}</span>
                      </li>
                    ))}
                  </ol>
                )}
                {section.tips && (
                  <ul className="flex flex-col gap-2">
                    {section.tips.map((line, n) => (
                      <li key={n} className="flex gap-3.5"><span aria-hidden className="mt-[9px] h-1.5 w-1.5 shrink-0 rounded-full bg-t3" /><span className="min-w-0">{rich(line)}</span></li>
                    ))}
                  </ul>
                )}
              </section>
            ))}

            <footer className="flex justify-between gap-3 border-t border-line pt-5">
              {before ? (
                <button type="button" onClick={() => setAt(before.id)} className={step}><span className="label-caps">Previous</span><span className="truncate text-[14px] font-medium">{before.title}</span></button>
              ) : <span />}
              {after && (
                <button type="button" onClick={() => setAt(after.id)} className={`${step} items-end text-right`}><span className="label-caps">Next</span><span className="truncate text-[14px] font-medium">{after.title}</span></button>
              )}
            </footer>
          </div>
        )}
      </article>
    </WindowShell>
  );
}
