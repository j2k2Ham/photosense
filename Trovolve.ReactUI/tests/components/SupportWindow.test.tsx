import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SupportWindow } from '../../components/SupportWindow';
import type { AboutDto } from '../../types';

const api = vi.hoisted(() => ({ useAbout: vi.fn(), useAboutText: vi.fn() }));
vi.mock('../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../lib/apiClient')>()), ...api }));

const about: AboutDto = {
  name: 'Trovolve', version: '0.1.0.76', program: 'server', publisher: 'Jamie Hamman', copyright: '© 2026 Jamie Hamman',
  supportEmail: 'help@trovolve.example', website: null, privacyPolicyUrl: null, termsUrl: null, supportUrl: null, downloadUrl: null,
};
const WINDOWS = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36';

let agent: ReturnType<typeof vi.spyOn>;
beforeEach(() => {
  api.useAbout.mockReset().mockReturnValue({ data: about });
  api.useAboutText.mockReset().mockReturnValue({ data: '# Support\n\n## Frequently asked questions\n\n### Where do files go?\n\nInto _Trovolve_Removed.\n' });
  agent = vi.spyOn(navigator, 'userAgent', 'get').mockReturnValue(WINDOWS);
});
afterEach(() => agent.mockRestore());

const show = (errors: string[] = []) => {
  const calls = { onHelp: vi.fn(), onErrors: vi.fn(), notify: vi.fn(), onClose: vi.fn() };
  render(<SupportWindow errors={errors.map(message => ({ message }))} {...calls} />);
  return calls;
};

describe('Support', () => {
  it('offers the guide, the errors and an email to support beside the common questions', async () => {
    const calls = show();
    expect(screen.getByRole('dialog', { name: 'Support' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Errors' })).toHaveTextContent('No errors this session.');
    expect(within(screen.getByRole('article', { name: 'Common questions' })).getByText('Where do files go?')).toBeInTheDocument();
    expect(api.useAboutText).toHaveBeenCalledWith('support');
    await userEvent.click(screen.getByRole('button', { name: 'Read the guide' }));
    expect(calls.onHelp).toHaveBeenCalledOnce();
    await userEvent.click(screen.getByRole('button', { name: 'See errors' }));
    expect(calls.onErrors).toHaveBeenCalledOnce();
  });

  it('starts an email to support with the version, the system and the latest errors, and asks what happened', () => {
    show(['First', 'Second', 'Third', 'Fourth']);
    expect(screen.getByRole('region', { name: 'Errors' })).toHaveTextContent('4 errors this session.');
    const details = 'Trovolve 0.1.0.76 for Windows and Mac\nSystem: Windows\nLatest errors:\n- Second\n- Third\n- Fourth';
    expect(screen.getByLabelText('Details for support').textContent).toBe(details);
    const href = screen.getByRole('link', { name: 'Email support' }).getAttribute('href')!;
    expect(href.startsWith('mailto:help@trovolve.example?subject=Trovolve%20support&body=')).toBe(true);
    expect(decodeURIComponent(href.split('&body=')[1])).toBe(`What I did:\n\nWhat I expected:\n\nWhat happened instead:\n\n---\n${details}\n`);
  });

  it('says one error as one', () => {
    show(['Only']);
    expect(screen.getByRole('region', { name: 'Errors' })).toHaveTextContent('1 error this session.');
  });

  it('copies the details and says so in a message, or says in one to copy them by hand when it cannot', async () => {
    const write = vi.fn().mockResolvedValueOnce(undefined).mockRejectedValueOnce(new Error('denied'));
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: write }, configurable: true });
    const { notify } = show();
    fireEvent.click(screen.getByRole('button', { name: 'Copy details' }));
    await waitFor(() => expect(notify).toHaveBeenCalledWith('Details copied.', 'ok'));
    expect(write).toHaveBeenCalledWith('Trovolve 0.1.0.76 for Windows and Mac\nSystem: Windows');
    fireEvent.click(screen.getByRole('button', { name: 'Copy details' }));
    await waitFor(() => expect(notify).toHaveBeenLastCalledWith('Trovolve could not copy the details. Select them above and copy them.', 'info'));
    // Nothing is said inside the window itself.
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('offers no email before the service has said where to write, or when there is no address', () => {
    api.useAbout.mockReturnValue({ data: undefined });
    show();
    expect(screen.queryByRole('link', { name: 'Email support' })).not.toBeInTheDocument();
    api.useAbout.mockReturnValue({ data: { ...about, supportEmail: '' } });
    show();
    expect(screen.queryByRole('link', { name: 'Email support' })).not.toBeInTheDocument();
  });

  it('closes on Escape and on Close, and on no other key', async () => {
    const calls = show();
    fireEvent.keyDown(window, { key: 'Enter' });
    expect(calls.onClose).not.toHaveBeenCalled();
    fireEvent.keyDown(window, { key: 'Escape' });
    await userEvent.click(screen.getByRole('button', { name: 'Close' }));
    expect(calls.onClose).toHaveBeenCalledTimes(2);
  });
});
