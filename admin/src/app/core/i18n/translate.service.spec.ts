import { TestBed } from '@angular/core/testing';
import { AuthService } from '../services/auth.service';
import { AuthServiceStub } from '../testing/auth-stub';
import { AR } from './ar';
import { EN } from './en';
import { TranslateService } from './translate.service';

describe('TranslateService', () => {
  let auth: AuthServiceStub;
  let service: TranslateService;

  beforeEach(() => {
    auth = new AuthServiceStub();

    TestBed.configureTestingModule({
      providers: [{ provide: AuthService, useValue: auth }]
    });

    service = TestBed.inject(TranslateService);
  });

  it('EN and AR catalogs have exactly the same keys', () => {
    const en = Object.keys(EN).sort();
    const ar = Object.keys(AR).sort();

    expect(ar.filter((k) => !(k in EN))).toEqual([]);
    expect(en.filter((k) => !(k in AR))).toEqual([]);
    expect(en.length).toBeGreaterThan(0);
  });

  it('no catalog entry is empty', () => {
    const empty = [...Object.entries(EN), ...Object.entries(AR)]
      .filter(([, v]) => !v || !v.trim())
      .map(([k]) => k);

    expect(empty).toEqual([]);
  });

  it('follows the active language signal', () => {
    auth.language.set('en');
    expect(service.t('nav.dashboard')).toBe(EN['nav.dashboard']);

    auth.language.set('ar');
    expect(service.t('nav.dashboard')).toBe(AR['nav.dashboard']);
  });

  it('falls back to English for an unknown language', () => {
    auth.language.set('fr');

    expect(service.t('nav.dashboard')).toBe(EN['nav.dashboard']);
  });

  it('returns the key itself for a missing key', () => {
    spyOn(console, 'warn');

    expect(service.t('does.not.exist')).toBe('does.not.exist');
    expect(console.warn).toHaveBeenCalled();
  });

  it('substitutes named parameters and leaves unknown placeholders intact', () => {
    const key = Object.keys(EN).find((k) => /\{\w+\}/.test(EN[k]));

    if (!key) {
      pending('no parameterised key in the catalog');
      return;
    }

    const placeholder = /\{(\w+)\}/.exec(EN[key])![1];
    const result = service.t(key, { [placeholder]: 'VALUE' });

    expect(result).toContain('VALUE');
    expect(result).not.toContain(`{${placeholder}}`);
  });
});
