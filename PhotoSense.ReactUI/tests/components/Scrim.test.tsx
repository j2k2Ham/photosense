import React, { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { Scrim } from '../../components/Scrim';

function Window({ name, onDismiss, children }: { readonly name: string; onDismiss(): void; readonly children?: React.ReactNode }) {
  return (
    <Scrim className="scrim" onDismiss={onDismiss}>
      <div role="dialog" aria-label={name}>{children}</div>
    </Scrim>
  );
}

/** A page with a button that opens a window, and a question that can be raised over it. */
function Page() {
  const [open, setOpen] = useState(false);
  const [asking, setAsking] = useState(false);
  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>Open</button>
      <button type="button">Behind</button>
      {open && (
        <Window name="Window" onDismiss={() => setOpen(false)}>
          <button type="button">First</button>
          <button type="button" disabled>Disabled</button>
          <button type="button" onClick={() => setAsking(true)}>Ask</button>
          <button type="button" onClick={() => setOpen(false)}>Last</button>
        </Window>
      )}
      {asking && (
        <Window name="Question" onDismiss={() => setAsking(false)}>
          <button type="button" onClick={() => setAsking(false)}>Yes</button>
          <button type="button">No</button>
        </Window>
      )}
    </>
  );
}

const button = (name: string) => screen.getByRole('button', { name });

describe('the layer a window stands on', () => {
  it('keeps Tab and Shift+Tab going round the window\'s own controls, past those switched off', async () => {
    render(<Page />);
    await userEvent.click(button('Open'));
    await userEvent.tab();
    expect(button('First')).toHaveFocus();
    await userEvent.tab();
    expect(button('Ask')).toHaveFocus();
    await userEvent.tab();
    expect(button('Last')).toHaveFocus();
    await userEvent.tab();
    expect(button('First')).toHaveFocus();
    await userEvent.tab({ shift: true });
    expect(button('Last')).toHaveFocus();
    await userEvent.tab({ shift: true });
    expect(button('Ask')).toHaveFocus();
    // Other keys are the window's own business.
    fireEvent.keyDown(document, { key: 'Enter' });
    expect(button('Ask')).toHaveFocus();
  });

  it('gives Tab to a question raised over the window, and back to the window once it goes', async () => {
    render(<Page />);
    await userEvent.click(button('Open'));
    await userEvent.click(button('Ask'));
    await userEvent.tab();
    expect(button('Yes')).toHaveFocus();
    await userEvent.tab();
    await userEvent.tab();
    expect(button('Yes')).toHaveFocus();
    await userEvent.click(button('Yes'));
    expect(screen.queryByRole('dialog', { name: 'Question' })).not.toBeInTheDocument();
    // Back on what raised the question.
    expect(button('Ask')).toHaveFocus();
    await userEvent.tab();
    expect(button('Last')).toHaveFocus();
  });

  it('puts focus back where it was once the window goes', async () => {
    render(<Page />);
    await userEvent.click(button('Open'));
    await userEvent.click(button('Last'));
    expect(button('Open')).toHaveFocus();
  });

  it('leaves focus alone when what had it is no longer there', () => {
    const { unmount } = render(<button type="button">Gone</button>);
    button('Gone').focus();
    const shown = render(<Window name="Window" onDismiss={vi.fn()} />);
    unmount();
    shown.unmount();
    expect(document.body).toHaveFocus();
  });

  it('holds Tab still in a window with nothing to focus', async () => {
    render(<><button type="button">Behind</button><Window name="Empty" onDismiss={vi.fn()} /></>);
    button('Behind').focus();
    await userEvent.tab();
    expect(button('Behind')).toHaveFocus();
  });

  it('puts the window away when pressed outside it, and not when pressed inside it', () => {
    const onDismiss = vi.fn();
    render(<Window name="Window" onDismiss={onDismiss}><button type="button">Inside</button></Window>);
    const layer = screen.getByRole('presentation');
    expect(layer).toHaveClass('scrim');
    fireEvent.mouseDown(button('Inside'));
    expect(onDismiss).not.toHaveBeenCalled();
    fireEvent.mouseDown(layer);
    expect(onDismiss).toHaveBeenCalledOnce();
  });
});
