import { Injectable, effect, signal } from '@angular/core';

export type ThemeMode = 'light' | 'dark';

const STORAGE_KEY = 'crockery.theme';

/**
 * Light or dark, remembered per machine.
 *
 * Stored in localStorage rather than on the user: the factory office is bright and the
 * owner's study is not, and the same account is used from both. It is a property of the
 * screen you are sitting at, so it belongs to the browser.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly current = signal<ThemeMode>(initial());

  readonly mode = this.current.asReadonly();

  constructor() {
    effect(() => {
      const mode = this.current();

      document.documentElement.classList.toggle('dark', mode === 'dark');

      try {
        localStorage.setItem(STORAGE_KEY, mode);
      } catch {
        // Private browsing, or storage full. The theme still applies for this session;
        // only remembering it fails, which is not worth an error to the user.
      }
    });
  }

  toggle(): void {
    this.current.update((mode) => (mode === 'dark' ? 'light' : 'dark'));
  }
}

/** A stored choice wins; otherwise follow whatever the operating system is set to. */
function initial(): ThemeMode {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === 'light' || stored === 'dark') return stored;
  } catch {
    // Fall through to the system preference.
  }

  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}
