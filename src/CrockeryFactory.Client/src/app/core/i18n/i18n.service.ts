import { Injectable, computed, effect, signal } from '@angular/core';
import { STRINGS, StringKey } from './strings';

export type Lang = 'en' | 'ur';

const STORAGE_KEY = 'crockery.lang';

/**
 * English and Urdu, switched at runtime.
 *
 * Deliberately not Angular's built-in $localize. That compiles one bundle per locale and
 * switching means loading a different URL, so the choice cannot follow a clerk who shares
 * a machine with the next shift. Here the language is a signal: flipping it re-renders
 * every visible string in place, and the choice is remembered per browser.
 *
 * `t` is an arrow property rather than a method so a component can hold it directly
 * (`protected readonly t = inject(I18nService).t`) and call `t('nav.stock')` in a template
 * without binding `this`. Reading the `lang` signal inside it is what makes a template
 * re-render on a language change - the dependency is registered by the read itself.
 */
@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly current = signal<Lang>(initial());

  readonly lang = this.current.asReadonly();
  readonly isUrdu = computed(() => this.current() === 'ur');

  /** Urdu is written right to left; everything else about the layout follows from this. */
  readonly dir = computed<'rtl' | 'ltr'>(() => (this.current() === 'ur' ? 'rtl' : 'ltr'));

  constructor() {
    effect(() => {
      const lang = this.current();
      const root = document.documentElement;

      root.lang = lang;
      root.dir = lang === 'ur' ? 'rtl' : 'ltr';

      try {
        localStorage.setItem(STORAGE_KEY, lang);
      } catch {
        // Private browsing. The choice still applies for this session.
      }
    });
  }

  /**
   * Looks up a string, substituting {name} placeholders.
   *
   * A missing key returns the key itself rather than an empty string: a screen reading
   * "stock.title" is an obvious bug, whereas a blank heading looks like a layout fault and
   * can survive review.
   */
  readonly t = (key: StringKey, params?: Record<string, string | number>): string => {
    const lang = this.current();
    const entry = STRINGS[key];

    if (!entry) return key;

    // Falling back to English rather than to the key: a string translated late should
    // still read as a sentence, just not yet in Urdu.
    // Annotated: the catalogue is `as const`, so without this `text` infers as the
    // literal union of every string in it and the placeholder substitution below
    // cannot be assigned back.
    let text: string = entry[lang] || entry.en || key;

    if (params) {
      for (const [name, value] of Object.entries(params)) {
        text = text.replaceAll(`{${name}}`, String(value));
      }
    }

    return text;
  };

  /**
   * Grades are enum values the server sends as 'First'/'Second'/'Third', not data the
   * factory typed, so they are part of the interface and translate with it.
   */
  readonly grade = (value: string | undefined): string =>
    value ? this.t(`grade.${value}` as StringKey) : '—';

  set(lang: Lang): void {
    this.current.set(lang);
  }

  toggle(): void {
    this.current.update((lang) => (lang === 'ur' ? 'en' : 'ur'));
  }
}

function initial(): Lang {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === 'en' || stored === 'ur') return stored;
  } catch {
    // Fall through.
  }

  // The factory is in Pakistan, but the browser is the better guess than a hard default.
  return navigator.language?.startsWith('ur') ? 'ur' : 'en';
}
