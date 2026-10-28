import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AboutWindow } from '../../components/AboutWindow';
import type { AboutDocument, AboutDto } from '../../types';

const api = vi.hoisted(() => ({ useAbout: vi.fn(), useAboutText: vi.fn() }));
vi.mock('../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../lib/apiClient')>()), ...api }));

const about: AboutDto = {
  name: 'PhotoSense', version: '0.1.0.76', program: 'server', publisher: 'Jamie Hamman', copyright: '© 2026 Jamie Hamman',
  supportEmail: 'help@photosense.example', website: 'https://photosense.example', privacyPolicyUrl: null, termsUrl: null, supportUrl: null,
};

const documents: Record<AboutDocument, string> = {
  support: '# Support\n\nChoose **Help** in the menu, or open `_PhotoSense_Removed`.\n\n## Common questions\n\n### Where do files go?\n\nSee [the policy](https://photosense.example/privacy).\n\n- which device\n- which version\n\n1. first\n2. second\n',
  privacy: '# Privacy Policy\n\nLast updated: October 28, 2026\n\nNothing is uploaded.',
  terms: '# Terms of Use\n\nUse it with care.',
  notices: 'Third-party notices for PhotoSense for Windows and Mac\n\n    LiteDB 5.0.21 (MIT)',
};

beforeEach(() => {
  api.useAbout.mockReset().mockReturnValue({ data: about });
  api.useAboutText.mockReset().mockImplementation((document: AboutDocument) => ({ data: documents[document] }));
});

const article = () => screen.getByRole('article');

describe('About PhotoSense', () => {
  it('opens on PhotoSense itself: its version for this program, who makes it, how to write for help, and its website', () => {
    render(<AboutWindow onClose={vi.fn()} />);
    expect(screen.getByRole('dialog', { name: 'About PhotoSense' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'PhotoSense' })).toHaveAttribute('aria-current', 'page');
    expect(within(article()).getByRole('heading', { level: 2, name: 'PhotoSense' })).toBeInTheDocument();
    expect(article()).toHaveTextContent('Version 0.1.0.76 for Windows and Mac');
    expect(article()).toHaveTextContent('PhotoSense works on this device. Your photos, and everything PhotoSense learns about them, stay here.');
    expect(article()).toHaveTextContent('Made byJamie Hamman');
    expect(screen.getByRole('link', { name: 'help@photosense.example' })).toHaveAttribute('href', 'mailto:help@photosense.example');
    const website = screen.getByRole('link', { name: 'https://photosense.example' });
    expect(website).toHaveAttribute('target', '_blank');
    expect(website).toHaveAttribute('rel', 'noreferrer');
    expect(article()).toHaveTextContent('© 2026 Jamie Hamman. All rights reserved.');
  });

  it('names the phone app as such, and leaves out a way to write and a website until they are set', () => {
    api.useAbout.mockReturnValue({ data: { ...about, program: 'iphone', supportEmail: null, website: null } });
    const { unmount } = render(<AboutWindow onClose={vi.fn()} />);
    expect(article()).toHaveTextContent('Version 0.1.0.76 for iPhone and iPad');
    expect(screen.queryByText('Support', { selector: 'dt' })).not.toBeInTheDocument();
    expect(screen.queryByText('Website', { selector: 'dt' })).not.toBeInTheDocument();
    unmount();

    api.useAbout.mockReturnValue({ data: { ...about, program: 'android' } });
    render(<AboutWindow onClose={vi.fn()} />);
    expect(screen.getByText('Version', { exact: false }).textContent).toBe('Version 0.1.0.76 ');
  });

  it('says it is loading, and why it could not load', () => {
    api.useAbout.mockReturnValue({ data: undefined });
    const { unmount } = render(<AboutWindow onClose={vi.fn()} />);
    expect(screen.getByRole('status')).toHaveTextContent('Loading…');
    unmount();

    api.useAbout.mockReturnValue({ data: undefined, error: new Error('The PhotoSense service is not answering.') });
    render(<AboutWindow onClose={vi.fn()} />);
    expect(screen.getByRole('alert')).toHaveTextContent('PhotoSense could not show this: The PhotoSense service is not answering.');
  });

  it('turns to a document and shows it as written, under the topic\'s own title, with links that open outside PhotoSense', async () => {
    render(<AboutWindow onClose={vi.fn()} />);
    await userEvent.click(screen.getByRole('button', { name: 'Support' }));

    expect(api.useAboutText).toHaveBeenLastCalledWith('support');
    expect(screen.getByRole('button', { name: 'Support' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('button', { name: 'PhotoSense' })).not.toHaveAttribute('aria-current');
    expect(within(article()).getAllByRole('heading').map(h => [h.tagName, h.textContent])).toEqual([['H2', 'Support'], ['H3', 'Common questions'], ['H4', 'Where do files go?']]);
    expect(within(article()).getByText('Help', { selector: 'strong' })).toBeInTheDocument();
    expect(within(article()).getByText('_PhotoSense_Removed', { selector: 'code' })).toBeInTheDocument();
    const link = within(article()).getByRole('link', { name: 'the policy' });
    expect(link).toHaveAttribute('href', 'https://photosense.example/privacy');
    expect(link).toHaveAttribute('target', '_blank');
    const [bullets, numbered] = within(article()).getAllByRole('list');
    expect(bullets.tagName).toBe('UL');
    expect(within(bullets).getAllByRole('listitem').map(i => i.textContent)).toEqual(['which device', 'which version']);
    expect(numbered.tagName).toBe('OL');

    await userEvent.click(screen.getByRole('button', { name: 'Privacy policy' }));
    expect(article()).toHaveTextContent('Last updated: October 28, 2026');
    await userEvent.click(screen.getByRole('button', { name: 'Terms of use' }));
    expect(article()).toHaveTextContent('Use it with care.');
  });

  it('shows the acknowledgements as the plain text they are written in', async () => {
    render(<AboutWindow onClose={vi.fn()} />);
    await userEvent.click(screen.getByRole('button', { name: 'Acknowledgements' }));
    const notices = article().querySelector('pre')!;
    expect(notices.textContent).toBe(documents.notices);
    expect(notices).toHaveClass('whitespace-pre-wrap', 'font-mono');
  });

  it('says a document is loading, and why it could not be shown', async () => {
    api.useAboutText.mockReturnValue({ data: undefined });
    render(<AboutWindow onClose={vi.fn()} />);
    await userEvent.click(screen.getByRole('button', { name: 'Terms of use' }));
    expect(screen.getByRole('status')).toHaveTextContent('Loading…');

    api.useAboutText.mockReturnValue({ data: undefined, error: new Error('There is no document called terms.') });
    await userEvent.click(screen.getByRole('button', { name: 'Privacy policy' }));
    expect(screen.getByRole('alert')).toHaveTextContent('PhotoSense could not show this: There is no document called terms.');
  });

  it('reads each topic from its top', async () => {
    render(<AboutWindow onClose={vi.fn()} />);
    article().scrollTop = 240;
    await userEvent.click(screen.getByRole('button', { name: 'Privacy policy' }));
    expect(article().scrollTop).toBe(0);
  });

  it('is put away with Escape or its close button', async () => {
    const onClose = vi.fn();
    const { unmount } = render(<AboutWindow onClose={onClose} />);
    fireEvent.keyDown(window, { key: 'Enter' });
    expect(onClose).not.toHaveBeenCalled();
    fireEvent.keyDown(window, { key: 'Escape' });
    await userEvent.click(screen.getByRole('button', { name: 'Close' }));
    expect(onClose).toHaveBeenCalledTimes(2);
    unmount();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(2);
  });
});
