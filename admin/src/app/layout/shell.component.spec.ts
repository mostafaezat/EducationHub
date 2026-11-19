import { Component } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter, Router } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ShellComponent } from './shell.component';

@Component({ template: '' })
class BlankPageComponent {}

/** QA-02: on phones the sidebar is an off-canvas drawer opened from the top bar. */
describe('ShellComponent mobile menu', () => {
  async function render() {
    TestBed.configureTestingModule({
      imports: [ShellComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        provideRouter([{ path: 'next', component: BlankPageComponent }]),
        MessageService,
        ConfirmationService
      ]
    });

    const fixture = TestBed.createComponent(ShellComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    const el = fixture.nativeElement as HTMLElement;
    const toggle = el.querySelector('.menu-toggle') as HTMLButtonElement;
    const shell = () => el.querySelector('.shell') as HTMLElement;

    return { fixture, el, toggle, shell };
  }

  it('opens from the menu button and closes from the backdrop', async () => {
    const { fixture, el, toggle, shell } = await render();

    expect(toggle).withContext('menu button rendered').not.toBeNull();
    expect(toggle.getAttribute('aria-controls')).toBe('app-sidebar');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(shell().classList).not.toContain('mobile-menu-open');
    expect(el.querySelector('.backdrop')).toBeNull();

    toggle.click();
    fixture.detectChanges();

    expect(shell().classList).toContain('mobile-menu-open');
    expect(toggle.getAttribute('aria-expanded')).toBe('true');

    (el.querySelector('.backdrop') as HTMLElement).click();
    fixture.detectChanges();

    expect(shell().classList).not.toContain('mobile-menu-open');
    expect(el.querySelector('.backdrop')).toBeNull();
  });

  it('closes after navigating to a page', async () => {
    const { fixture, toggle, shell } = await render();

    toggle.click();
    fixture.detectChanges();
    expect(shell().classList).toContain('mobile-menu-open');

    await TestBed.inject(Router).navigateByUrl('/next');
    fixture.detectChanges();

    expect(shell().classList).not.toContain('mobile-menu-open');
  });

  it('shows the full labels in the drawer even when the desktop sidebar was collapsed', async () => {
    const { fixture, el, toggle, shell } = await render();

    (el.querySelector('.collapse-toggle') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(shell().classList).toContain('sidebar-collapsed');

    toggle.click();
    fixture.detectChanges();

    expect(shell().classList).not.toContain('sidebar-collapsed');
    expect(el.querySelector('.brand span')?.textContent).toContain('EducationHub');
  });
});
