import React, { useCallback, useState } from 'react';
import { useBackLayer } from '../../lib/backStack';
import { useForm, WORDS, type Platform, type TabId } from '../../lib/phone';
import type { ThemeChoice } from '../../lib/theme';
import type { Toast } from '../Toaster';
import { PhoneShell } from './PhoneShell';
import { RootHeader, Screen } from './Screens';
import { SettingsSheet } from './SettingsSheet';

interface Props {
  readonly platform: Platform;
  readonly tab: TabId;
  /** A group is being reviewed on its own screen, pushed over Clean up's list. */
  readonly reviewing: boolean;
  /** Clean up's own screens: scanning, or the groups found and their review. */
  readonly cleanup: React.ReactNode;
  /** Organize's own screens. */
  readonly organize: React.ReactNode;
  /** Clean up's subtitle, by which groups are shown. */
  readonly cleanupSubtitle: string;
  /** Something the person should know above every screen, such as the service not answering. */
  readonly notice?: React.ReactNode;
  readonly counts: Partial<Record<TabId, string>>;
  readonly toasts: Toast[];
  readonly theme: ThemeChoice;
  readonly version?: string;
  readonly errorCount: number;
  onTab(tab: TabId): void;
  onEndReview(): void;
  removeToast(id: number): void;
  onTheme(theme: ThemeChoice): void;
  onScanAgain(): void;
  onDeletePermanently(): void;
  onErrors(): void;
  onHelp(): void;
  onAbout(): void;
}

/**
 * PhotoSense on a phone or tablet: the four tabs in the shell, each with its header and the gear that opens Settings.
 * Clean up and Organize show their screens as they are made today; Library and Send are new, and come step by step.
 */
export function PhoneApp(props: Props) {
  const { platform, tab, reviewing, cleanup, organize, cleanupSubtitle, notice, counts, toasts, theme, version, errorCount, onTab, onEndReview, removeToast } = props;
  const [settingsOpen, setSettingsOpen] = useState(false);
  const openSettings = useCallback(() => setSettingsOpen(true), []);
  const words = WORDS[platform];
  // On a phone a review is pushed over the list, and Back, or a swipe from the edge, takes it back; a tablet shows both.
  const form = useForm();
  const pushed = reviewing && tab === 'cleanup' && form === 'phone';
  useBackLayer(pushed, onEndReview);

  return (
    <PhoneShell platform={platform} tab={tab} foot={pushed ? 'none' : 'tabs'} counts={counts} toasts={toasts} onTab={onTab} onSettings={openSettings} removeToast={removeToast}
      notice={notice}
      screens={{
        library: <Screen label="Library"><RootHeader title="Library" /></Screen>,
        cleanup: <Screen label="Clean up" fixed>{!pushed && <RootHeader title="Clean up" subtitle={cleanupSubtitle} />}{cleanup}</Screen>,
        // Until Organize is rebuilt for phones, its screen scrolls as a whole, at least as tall as a phone held upright,
        // so that on a short screen nothing of it stays under the tab bar.
        organize: <Screen label="Organize"><RootHeader title="Organize" subtitle={`Sort ${words.library} into ${words.albums} by place and date`} /><div className="flex h-[max(600px,100%)] shrink-0 flex-col">{organize}</div></Screen>,
        send: <Screen label="Send to computer"><RootHeader title="Send to computer" subtitle="Copy or move to PhotoSense on a Windows PC or Mac" /></Screen>,
      }}
      sheet={settingsOpen && (
        <SettingsSheet theme={theme} version={version} errorCount={errorCount} onTheme={props.onTheme} onScanAgain={props.onScanAgain} onDeletePermanently={props.onDeletePermanently}
          onErrors={props.onErrors} onHelp={props.onHelp} onAbout={props.onAbout} onClose={() => setSettingsOpen(false)} />
      )} />
  );
}
