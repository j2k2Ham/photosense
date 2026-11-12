import React from 'react';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Facts, HOVER_DELAY, useHoverCard } from '../../components/HoverCard';

function Gallery({ width }: { readonly width?: number }) {
  const hover = useHoverCard<string>();
  return (
    <>
      {['one', 'two'].map(name => <button key={name} type="button" {...hover.on(name, name)}>{name}</button>)}
      {hover.card(name => <p>About {name}</p>, width)}
    </>
  );
}

const tile = (name: string) => screen.getByRole('button', { name });
const card = () => screen.queryByRole('tooltip');
const point = (name: string, pointerType = 'mouse') => fireEvent.pointerEnter(tile(name), { pointerType });
const wait = (ms: number) => act(() => { vi.advanceTimersByTime(ms); });
// Where a tile is in the window, which the test window makes 1024 by 768.
const placeTile = (name: string, left: number, top: number, size = 100) =>
  vi.spyOn(tile(name), 'getBoundingClientRect').mockReturnValue({ left, top, width: size, height: size, right: left + size, bottom: top + size } as DOMRect);

beforeEach(() => { vi.useFakeTimers(); });
afterEach(() => { vi.useRealTimers(); vi.restoreAllMocks(); });

describe('the card of details beside a tile', () => {
  it('waits for the pointer to rest on a tile, then says what it is about, described to whoever reads the tile', () => {
    render(<Gallery />);
    point('one');
    wait(HOVER_DELAY - 1);
    expect(card()).not.toBeInTheDocument();
    wait(1);
    expect(card()).toHaveTextContent('About one');
    expect(card()!.parentElement).toBe(document.body);
    expect(tile('one')).toHaveAttribute('aria-describedby', card()!.id);
    expect(tile('two')).not.toHaveAttribute('aria-describedby');
  });

  it('comes at once for the next tile once one is up', () => {
    render(<Gallery />);
    point('one');
    wait(HOVER_DELAY);
    fireEvent.pointerLeave(tile('one'));
    point('two');
    wait(0);
    expect(card()).toHaveTextContent('About two');
  });

  it('never opens at a touch', () => {
    render(<Gallery />);
    point('one', 'touch');
    wait(HOVER_DELAY * 2);
    expect(card()).not.toBeInTheDocument();
  });

  it('stays a moment after the pointer leaves, and for as long as the pointer rests on it', () => {
    render(<Gallery />);
    point('one');
    wait(HOVER_DELAY);
    fireEvent.pointerLeave(tile('one'));
    wait(100);
    fireEvent.pointerEnter(card()!);
    wait(1000);
    expect(card()).toBeInTheDocument();
    fireEvent.pointerLeave(card()!);
    wait(149);
    expect(card()).toBeInTheDocument();
    wait(1);
    expect(card()).not.toBeInTheDocument();
  });

  it('goes at a press, and does not come back with the focus the press brings', () => {
    render(<Gallery />);
    point('one');
    wait(HOVER_DELAY);
    fireEvent.pointerDown(tile('one'));
    expect(card()).not.toBeInTheDocument();
    fireEvent.focus(tile('one'));
    expect(card()).not.toBeInTheDocument();
    // Reached by the keyboard afterwards, it comes at once; it goes when the keyboard moves on.
    fireEvent.blur(tile('one'));
    fireEvent.focus(tile('two'));
    expect(card()).toHaveTextContent('About two');
    fireEvent.blur(tile('two'));
    expect(card()).not.toBeInTheDocument();
  });

  it('is put away by Esc, which then does nothing else, and stays up through other keys', () => {
    const elsewhere = vi.fn();
    window.addEventListener('keydown', elsewhere);
    render(<Gallery />);
    fireEvent.focus(tile('one'));
    fireEvent.keyDown(tile('one'), { key: 'Enter' });
    expect(card()).toBeInTheDocument();
    expect(elsewhere).toHaveBeenCalledOnce();
    fireEvent.keyDown(tile('one'), { key: 'Escape' });
    expect(card()).not.toBeInTheDocument();
    expect(elsewhere).toHaveBeenCalledOnce();
    // With no card up, Esc is for whatever else wants it.
    fireEvent.keyDown(tile('one'), { key: 'Escape' });
    expect(elsewhere).toHaveBeenCalledTimes(2);
    window.removeEventListener('keydown', elsewhere);
  });

  it('goes when what is under it scrolls, or the window changes size', () => {
    render(<Gallery />);
    fireEvent.focus(tile('one'));
    fireEvent.scroll(document.body);
    expect(card()).not.toBeInTheDocument();
    fireEvent.blur(tile('one'));
    fireEvent.focus(tile('one'));
    fireEvent(window, new Event('resize'));
    expect(card()).not.toBeInTheDocument();
  });

  it('forgets a card still to come when the gallery goes', () => {
    const { unmount } = render(<Gallery />);
    point('one');
    unmount();
    wait(HOVER_DELAY);
    expect(card()).not.toBeInTheDocument();
  });
});

describe('where the card is put', () => {
  const tall = (height: number) => vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(height);

  it('above the tile, centred on it, when there is room there', () => {
    tall(200);
    render(<Gallery />);
    placeTile('one', 400, 500);
    fireEvent.focus(tile('one'));
    expect(card()).toHaveStyle({ left: '280px', top: '292px', width: '340px', visibility: 'visible' });
  });

  it('below it when there is no room above, and held inside the window at either side', () => {
    tall(200);
    render(<Gallery width={300} />);
    placeTile('one', 2, 40);
    fireEvent.focus(tile('one'));
    expect(card()).toHaveStyle({ left: '12px', top: '148px', width: '300px' });
    fireEvent.blur(tile('one'));
    placeTile('two', 990, 40);
    fireEvent.focus(tile('two'));
    expect(card()).toHaveStyle({ left: '712px' });
  });

  it('against the top of the window when it fits neither above nor below, and never wider than the window', () => {
    tall(900);
    render(<Gallery width={2000} />);
    placeTile('one', 400, 300);
    fireEvent.focus(tile('one'));
    expect(card()).toHaveStyle({ left: '12px', top: '12px', width: '1000px' });
  });
});

describe('the details on a card', () => {
  it('are a label beside each, paths in the monospaced face', () => {
    render(<Facts rows={[['Taken', 'Jul 4, 2022'], ['Folder', 'C:\\photos', true]]} />);
    expect(screen.getByText('Taken').nextElementSibling).not.toHaveClass('font-mono');
    expect(screen.getByText('Folder').nextElementSibling).toHaveClass('font-mono');
  });
});
