import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ContinueQuestion } from '../../components/ContinueQuestion';

/** Shows the question, with stand-ins for what it is told. */
function show() {
  const handlers = { onAnswer: vi.fn(), onClose: vi.fn() };
  render(<ContinueQuestion {...handlers} />);
  return handlers;
}

describe('the question whether to continue where the person left off', () => {
  it('says what each answer does, with continuing ready to press', () => {
    show();
    const question = screen.getByRole('alertdialog', { name: 'Continue where you left off?' });
    expect(question).toHaveTextContent('Next time Trovolve opens, it can show your last results in Clean up and your last folder in Organize.');
    expect(question).toHaveTextContent('Or it can start fresh each time: the last results forgotten, and no folder chosen. Your photos are never touched.');
    expect(screen.getByRole('button', { name: 'Continue where I left off' })).toHaveFocus();
  });

  it.each([['Continue where I left off', true], ['Start fresh each time', false]] as const)('keeps the answer pressed: %s', async (name, carryOn) => {
    const { onAnswer, onClose } = show();
    await userEvent.click(screen.getByRole('button', { name }));
    expect(onAnswer).toHaveBeenCalledExactlyOnceWith(carryOn);
    expect(onClose).not.toHaveBeenCalled();
  });

  it('goes unanswered on Escape or a press outside, and on nothing else', () => {
    const { onAnswer, onClose } = show();
    fireEvent.keyDown(window, { key: 'Enter' });
    expect(onClose).not.toHaveBeenCalled();
    fireEvent.keyDown(window, { key: 'Escape' });
    fireEvent.mouseDown(screen.getByRole('alertdialog'));
    expect(onClose).toHaveBeenCalledOnce();
    fireEvent.mouseDown(screen.getByRole('presentation'));
    expect(onClose).toHaveBeenCalledTimes(2);
    expect(onAnswer).not.toHaveBeenCalled();
  });
});
