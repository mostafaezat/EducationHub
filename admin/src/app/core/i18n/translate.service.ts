import { computed, inject, Injectable } from '@angular/core';
import { AuthService } from '../services/auth.service';
import { AR } from './ar';
import { EN } from './en';

export type TranslateParams = Record<string, string | number>;

const CATALOGS: Record<string, Record<string, string>> = { ar: AR, en: EN };
const FALLBACK = 'en';

/**
 * The app's own translation layer - deliberately tiny: two static catalogs, a signal for
 * the active language (owned by AuthService) and a key lookup with parameter substitution.
 *
 * <p>Why static TypeScript catalogs instead of fetched JSON: switching language must never
 * race a network request or flash untranslated keys, and the build fails loudly if a
 * catalog is malformed. Both languages cost a few dozen kB in the bundle.</p>
 *
 * <p>Server messages are NOT translated here - the API localizes its own ProblemDetails
 * and Result messages from the <c>Accept-Language</c> header. This service only covers
 * strings the SPA itself owns.</p>
 */
@Injectable({ providedIn: 'root' })
export class TranslateService {
  private readonly auth = inject(AuthService);

  private readonly catalog = computed(() => CATALOGS[this.auth.language()] ?? CATALOGS[FALLBACK]);

  /**
   * Key lookup usable from both templates and class code. Reading the catalog signal
   * inside a template expression makes that component a signal consumer, so a language
   * switch re-renders every bound node automatically.
   */
  readonly t = (key: string, params?: TranslateParams): string => {
    const text = this.catalog()[key] ?? EN[key];

    if (text === undefined) {
      // Loud in dev so a missing key is caught during development, not shipped.
      console.warn(`[i18n] missing key: ${key}`);
      return key;
    }

    if (!params) {
      return text;
    }

    return text.replace(/\{(\w+)\}/g, (placeholder, name: string) =>
      Object.prototype.hasOwnProperty.call(params, name) ? String(params[name]) : placeholder
    );
  };
}
