import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { errorMessage } from '@functions/errors';
import { AuthService } from '@services/auth.service';

// Opening a magic link signs the person in and takes them to that trip (requirements 3.2).
@Component({
  selector: 'app-magic-link',
  imports: [RouterLink],
  template: `
    <section class="magic">
      @if (error(); as e) {
        <div class="magic__card card">
          <h1>That link didn't work</h1>
          <p class="muted">{{ e }}</p>
          <a class="btn btn--primary" routerLink="/">Go to Vamoose</a>
        </div>
      } @else {
        <p class="muted" role="status">Opening your trip…</p>
      }
    </section>
  `,
  styles: `
    .magic {
      display: grid;
      place-items: center;
      min-height: 60dvh;
      padding: var(--space-4);
      text-align: center;
    }
    .magic__card {
      max-width: 420px;
      padding: var(--space-6) var(--space-5);
    }
    .magic__card p {
      margin: var(--space-2) 0 var(--space-4);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MagicLink implements OnInit {
  private readonly _auth = inject(AuthService);
  private readonly _router = inject(Router);

  readonly token = input.required<string>();
  protected readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      const result = await this._auth.openMagicLink(this.token());
      await this._router.navigate(['/trips', result.tripId], { replaceUrl: true });
    } catch (e) {
      this.error.set(errorMessage(e));
    }
  }
}
