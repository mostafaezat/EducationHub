import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AuthService } from './core/services/auth.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: '<router-outlet />'
})
export class App {
  constructor() {
    const auth = inject(AuthService);

    // The stored language must land on <html> before anything renders, otherwise a
    // returning Arabic user gets a flash of LTR on first paint.
    auth.applyStoredLanguage();

    // A stored session is revalidated up-front so a revoked refresh token cannot leave
    // a phantom logged-in shell behind.
    auth.bootstrapSession().subscribe();
  }
}
