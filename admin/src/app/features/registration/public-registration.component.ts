import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { TranslateService } from '../../core/i18n/translate.service';
import { GUARDIAN_RELATIONSHIPS, GuardianRelationship, PublicOptionDto, PublicRegistrationFormDto } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { RegistrationService } from '../../core/services/registration.service';

type PageState = 'loading' | 'form' | 'done' | 'notFound' | 'loadFailed';

/**
 * The centre's public pre-registration page (`/register/:tenantSlug`). Anonymous, Arabic first,
 * built for a phone screen: a parent opens the link from WhatsApp, fills one column and sends.
 */
@Component({
  selector: 'app-public-registration',
  imports: [CommonModule, ReactiveFormsModule, ButtonModule, InputTextModule, MessageModule, SelectModule, TextareaModule],
  template: `
    <div class="page">
      <header class="top">
        <div class="brand">
          <span class="badge" aria-hidden="true">{{ initials() }}</span>
          <div>
            <div class="centre">{{ centreName() }}</div>
            <div class="caption">{{ t('register.title') }}</div>
          </div>
        </div>
        <button type="button" class="lang" (click)="toggleLanguage()">{{ t('register.switchLanguage') }}</button>
      </header>

      <main class="card">
        @switch (state()) {
          @case ('loading') {
            <div class="center"><i class="pi pi-spin pi-spinner"></i></div>
          }
          @case ('notFound') {
            <div class="center">
              <i class="pi pi-exclamation-circle big"></i>
              <h1>{{ t('register.notFoundTitle') }}</h1>
              <p class="muted">{{ t('register.notFoundBody') }}</p>
            </div>
          }
          @case ('loadFailed') {
            <div class="center">
              <p-message severity="error" [text]="t('register.loadFailed')" />
              <p-button [label]="t('register.retry')" icon="pi pi-refresh" (onClick)="load()" />
            </div>
          }
          @case ('done') {
            <div class="center">
              <i class="pi pi-check-circle big ok"></i>
              <h1>{{ t('register.successTitle') }}</h1>
              <p>{{ t('register.successBody', { name: submittedGuardian(), centre: centreName(), student: submittedStudent() }) }}</p>
              <p-button [label]="t('register.another')" icon="pi pi-plus" [outlined]="true" (onClick)="another()" />
            </div>
          }
          @case ('form') {
            <h1>{{ t('register.title') }}</h1>
            <p class="muted">{{ t('register.subtitle') }}</p>

            <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
              <!-- Honeypot: hidden from people, filled by bots. -->
              <div class="hp" aria-hidden="true">
                <label for="website">Website</label>
                <input id="website" type="text" formControlName="website" tabindex="-1" autocomplete="off" />
              </div>

              <fieldset>
                <legend>{{ t('register.guardianSection') }}</legend>

                <div class="field">
                  <label for="guardianName">{{ t('register.guardianName') }} *</label>
                  <input id="guardianName" pInputText formControlName="guardianName" autocomplete="name" />
                  @if (invalid('guardianName')) {
                    <small class="err">{{ t('register.required') }}</small>
                  }
                </div>

                <div class="field">
                  <label for="guardianPhone">{{ t('register.guardianPhone') }} *</label>
                  <input
                    id="guardianPhone"
                    pInputText
                    type="tel"
                    dir="ltr"
                    inputmode="tel"
                    autocomplete="tel"
                    placeholder="01012345678"
                    formControlName="guardianPhone"
                  />
                  @if (invalid('guardianPhone')) {
                    <small class="err">{{ t('register.phoneInvalid') }}</small>
                  } @else {
                    <small class="muted">{{ t('register.guardianPhoneHint') }}</small>
                  }
                </div>

                <div class="field">
                  <label for="relationship">{{ t('register.relationship') }}</label>
                  <p-select
                    inputId="relationship"
                    [options]="relationships()"
                    optionLabel="label"
                    optionValue="value"
                    formControlName="relationship"
                    styleClass="w-full"
                  />
                </div>

                <div class="field">
                  <label for="guardianEmail">{{ t('register.guardianEmail') }} <span class="muted">{{ t('register.optional') }}</span></label>
                  <input id="guardianEmail" pInputText type="email" dir="ltr" autocomplete="email" formControlName="guardianEmail" />
                </div>
              </fieldset>

              <fieldset>
                <legend>{{ t('register.studentSection') }}</legend>

                <div class="field">
                  <label for="studentName">{{ t('register.studentName') }} *</label>
                  <input id="studentName" pInputText formControlName="studentName" />
                  @if (invalid('studentName')) {
                    <small class="err">{{ t('register.required') }}</small>
                  }
                </div>

                <div class="row">
                  <div class="field">
                    <label for="birthDate">{{ t('register.birthDate') }} <span class="muted">{{ t('register.optional') }}</span></label>
                    <input id="birthDate" pInputText type="date" [max]="today" formControlName="birthDate" />
                  </div>

                  @if (grades().length) {
                    <div class="field">
                      <label for="grade">{{ t('register.grade') }}</label>
                      <p-select
                        inputId="grade"
                        [options]="grades()"
                        optionLabel="label"
                        optionValue="value"
                        formControlName="gradeLevelId"
                        [placeholder]="t('register.selectGrade')"
                        [showClear]="true"
                        styleClass="w-full"
                      />
                    </div>
                  }
                </div>

                <div class="field">
                  <label for="school">{{ t('register.school') }} <span class="muted">{{ t('register.optional') }}</span></label>
                  <input id="school" pInputText formControlName="school" />
                </div>

                @if (subjects().length) {
                  <div class="field">
                    <span class="label">{{ t('register.subjects') }}</span>
                    <div class="chips">
                      @for (s of subjects(); track s.id) {
                        <button
                          type="button"
                          class="chip"
                          [class.on]="chosen().has(s.id)"
                          [attr.aria-pressed]="chosen().has(s.id)"
                          (click)="toggleSubject(s.id)"
                        >
                          {{ optionName(s) }}
                        </button>
                      }
                    </div>
                  </div>
                }

                <div class="field">
                  <label for="notes">{{ t('register.notes') }} <span class="muted">{{ t('register.optional') }}</span></label>
                  <textarea id="notes" pTextarea rows="3" formControlName="notes" [placeholder]="t('register.notesPlaceholder')"></textarea>
                </div>
              </fieldset>

              @if (error()) {
                <p-message severity="error" [text]="error()!" />
              }

              <p-button type="submit" [label]="t('register.submit')" icon="pi pi-send" [loading]="sending()" styleClass="w-full submit" />
            </form>
          }
        }
      </main>
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
        min-height: 100vh;
        background: #f4f6fa;
      }
      .page {
        max-width: 560px;
        margin: 0 auto;
        padding: 1rem 1rem 2rem;
      }
      .top {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 0.75rem;
        margin-bottom: 1rem;
      }
      .brand {
        display: flex;
        align-items: center;
        gap: 0.75rem;
        min-width: 0;
      }
      .badge {
        flex: none;
        width: 2.75rem;
        height: 2.75rem;
        border-radius: 50%;
        display: grid;
        place-items: center;
        background: var(--p-primary-color, #2563eb);
        color: #fff;
        font-weight: 700;
      }
      .centre {
        font-weight: 700;
        font-size: 1.05rem;
        overflow-wrap: anywhere;
      }
      .caption {
        color: #64748b;
        font-size: 0.85rem;
      }
      .lang {
        flex: none;
        border: 1px solid #cbd5e1;
        background: #fff;
        border-radius: 999px;
        padding: 0.35rem 0.8rem;
        cursor: pointer;
        font: inherit;
      }
      .card {
        background: #fff;
        border-radius: 12px;
        padding: 1.25rem;
        box-shadow: 0 1px 3px rgba(15, 23, 42, 0.08);
      }
      h1 {
        font-size: 1.25rem;
        margin: 0 0 0.25rem;
      }
      .muted {
        color: #64748b;
        font-size: 0.85rem;
        font-weight: normal;
      }
      form {
        display: flex;
        flex-direction: column;
        gap: 1rem;
        margin-top: 1rem;
      }
      fieldset {
        border: 0;
        padding: 0;
        margin: 0;
        display: flex;
        flex-direction: column;
        gap: 0.9rem;
      }
      legend {
        font-weight: 700;
        margin-bottom: 0.6rem;
        color: #0f172a;
      }
      .field {
        display: flex;
        flex-direction: column;
        gap: 0.35rem;
        flex: 1;
        min-width: 0;
      }
      .row {
        display: flex;
        gap: 0.75rem;
        flex-wrap: wrap;
      }
      .row .field {
        min-width: 12rem;
      }
      label,
      .label {
        font-weight: 600;
        font-size: 0.9rem;
      }
      input[pInputText],
      textarea {
        width: 100%;
        font-size: 1rem;
      }
      .err {
        color: #dc2626;
      }
      .chips {
        display: flex;
        flex-wrap: wrap;
        gap: 0.5rem;
      }
      .chip {
        border: 1px solid #cbd5e1;
        background: #fff;
        border-radius: 999px;
        padding: 0.45rem 0.9rem;
        font: inherit;
        cursor: pointer;
      }
      .chip.on {
        background: var(--p-primary-color, #2563eb);
        border-color: var(--p-primary-color, #2563eb);
        color: #fff;
      }
      .center {
        display: flex;
        flex-direction: column;
        align-items: center;
        text-align: center;
        gap: 0.75rem;
        padding: 1.5rem 0;
      }
      .big {
        font-size: 3rem;
        color: #94a3b8;
      }
      .big.ok {
        color: #16a34a;
      }
      .hp {
        position: absolute;
        left: -10000px;
        width: 1px;
        height: 1px;
        overflow: hidden;
      }
      :host ::ng-deep .w-full {
        width: 100%;
      }
      :host ::ng-deep .submit {
        padding: 0.8rem;
        font-size: 1.05rem;
      }
    `
  ]
})
export class PublicRegistrationComponent implements OnInit {
  protected readonly t = inject(TranslateService).t;
  private readonly auth = inject(AuthService);
  private readonly service = inject(RegistrationService);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);

  protected readonly today = new Date().toISOString().slice(0, 10);
  protected readonly state = signal<PageState>('loading');
  protected readonly info = signal<PublicRegistrationFormDto | null>(null);
  protected readonly sending = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly chosen = signal<ReadonlySet<string>>(new Set());
  protected readonly submittedGuardian = signal('');
  protected readonly submittedStudent = signal('');

  private readonly arabic = computed(() => this.auth.language() === 'ar');

  protected readonly centreName = computed(() => {
    const info = this.info();
    return info ? (this.arabic() && info.centreNameAr ? info.centreNameAr : info.centreName) : '';
  });

  protected readonly initials = computed(() =>
    this.centreName()
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((w) => w[0])
      .join('')
      .toUpperCase()
  );

  protected readonly relationships = computed(() =>
    GUARDIAN_RELATIONSHIPS.map((r) => ({ label: this.t(`students.relationship.${r}`), value: r }))
  );

  protected readonly grades = computed(() =>
    (this.info()?.gradeLevels ?? []).map((g) => ({ label: this.optionName(g), value: g.id }))
  );

  protected readonly subjects = computed(() => this.info()?.subjects ?? []);

  protected readonly form = this.fb.nonNullable.group({
    guardianName: ['', [Validators.required, Validators.maxLength(200)]],
    guardianPhone: ['', [Validators.required, Validators.pattern(/^[+0-9 ()-]{8,20}$/)]],
    guardianEmail: ['', [Validators.email, Validators.maxLength(256)]],
    relationship: ['Father' as GuardianRelationship],
    studentName: ['', [Validators.required, Validators.maxLength(200)]],
    birthDate: [''],
    gradeLevelId: [null as string | null],
    school: ['', Validators.maxLength(200)],
    notes: ['', Validators.maxLength(1000)],
    website: ['']
  });

  private slug = '';

  ngOnInit(): void {
    this.slug = this.route.snapshot.paramMap.get('tenantSlug') ?? '';
    this.auth.applyStoredLanguage();
    this.load();
  }

  protected load(): void {
    this.state.set('loading');

    this.service.form(this.slug).subscribe({
      next: (info) => {
        this.info.set(info);
        this.state.set('form');
      },
      error: (err: unknown) =>
        this.state.set(err instanceof HttpErrorResponse && err.status === 404 ? 'notFound' : 'loadFailed')
    });
  }

  protected optionName(option: PublicOptionDto): string {
    return this.arabic() && option.nameAr ? option.nameAr : option.name;
  }

  protected toggleLanguage(): void {
    this.auth.setLanguage(this.arabic() ? 'en' : 'ar');
  }

  protected toggleSubject(id: string): void {
    const next = new Set(this.chosen());
    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }
    this.chosen.set(next);
  }

  protected invalid(name: 'guardianName' | 'guardianPhone' | 'studentName'): boolean {
    const control = this.form.controls[name];
    return control.invalid && control.touched;
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const v = this.form.getRawValue();
    this.sending.set(true);
    this.error.set(null);

    this.service
      .submit(this.slug, {
        guardianName: v.guardianName.trim(),
        guardianPhone: v.guardianPhone.trim(),
        guardianEmail: v.guardianEmail.trim() || null,
        relationship: v.relationship,
        studentName: v.studentName.trim(),
        birthDate: v.birthDate || null,
        gradeLevelId: v.gradeLevelId,
        school: v.school.trim() || null,
        subjectIds: [...this.chosen()],
        notes: v.notes.trim() || null,
        website: v.website || null
      })
      .subscribe({
        next: () => {
          this.sending.set(false);
          this.submittedGuardian.set(v.guardianName.trim());
          this.submittedStudent.set(v.studentName.trim());
          this.state.set('done');
        },
        error: (err: unknown) => {
          this.sending.set(false);
          const problem =
            err instanceof HttpErrorResponse && err.error && typeof err.error === 'object'
              ? (err.error as { detail?: string; errors?: { message?: string }[] })
              : null;

          if (err instanceof HttpErrorResponse && err.status === 429) {
            this.error.set(this.t('register.tooMany'));
          } else if (err instanceof HttpErrorResponse && err.status === 404) {
            this.state.set('notFound');
          } else {
            this.error.set(problem?.errors?.[0]?.message ?? problem?.detail ?? this.t('register.submitFailed'));
          }
        }
      });
  }

  /** Same parent, another child: keep the guardian's details. */
  protected another(): void {
    this.form.patchValue({ studentName: '', birthDate: '', gradeLevelId: null, school: '', notes: '' });
    this.form.markAsUntouched();
    this.chosen.set(new Set());
    this.state.set('form');
  }
}
