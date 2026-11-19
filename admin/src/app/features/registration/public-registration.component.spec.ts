import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { LANGUAGE_KEY } from '../../core/app-config';
import { RegistrationService } from '../../core/services/registration.service';
import { PublicRegistrationComponent } from './public-registration.component';

/** QA-03: the hidden honeypot must not widen the public page, in particular in Arabic (RTL). */
describe('PublicRegistrationComponent layout', () => {
  afterEach(() => {
    localStorage.removeItem(LANGUAGE_KEY);
    document.documentElement.removeAttribute('dir');
    document.documentElement.removeAttribute('lang');
  });

  for (const lang of ['ar', 'en']) {
    it(`keeps the honeypot inside the viewport and the page unscrolled sideways (${lang})`, () => {
      localStorage.setItem(LANGUAGE_KEY, lang);

      TestBed.configureTestingModule({
        imports: [PublicRegistrationComponent],
        providers: [
          provideHttpClient(),
          provideHttpClientTesting(),
          provideNoopAnimations(),
          { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ tenantSlug: 'demo' }) } } },
          {
            provide: RegistrationService,
            useValue: {
              form: () => of({ tenantSlug: 'demo', centreName: 'Demo', centreNameAr: 'تجريبي', gradeLevels: [], subjects: [] }),
              submit: () => of('ok')
            }
          }
        ]
      });

      const fixture = TestBed.createComponent(PublicRegistrationComponent);
      document.body.appendChild(fixture.nativeElement);
      fixture.detectChanges();

      try {
        expect(document.documentElement.dir).toBe(lang === 'ar' ? 'rtl' : 'ltr');

        const honeypot = fixture.nativeElement.querySelector('.hp') as HTMLElement;
        expect(honeypot).withContext('honeypot rendered').not.toBeNull();

        const box = honeypot.getBoundingClientRect();
        expect(box.left).toBeGreaterThanOrEqual(-1);
        expect(box.right).toBeLessThanOrEqual(document.documentElement.clientWidth + 1);

        // The honeypot field is still in the form for bots to fill.
        expect(honeypot.querySelector('input#website')).not.toBeNull();

        expect(document.documentElement.scrollWidth - document.documentElement.clientWidth).toBeLessThanOrEqual(1);
      } finally {
        fixture.nativeElement.remove();
        fixture.destroy();
      }
    });
  }
});
