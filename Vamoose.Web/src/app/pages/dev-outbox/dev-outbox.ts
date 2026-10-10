import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { OutboxEmail } from '@interfaces/api';
import { TripService } from '@services/trip.service';

interface OutboxRow extends OutboxEmail {
  path: string | null;
}

// Development only: the emails the app would have sent, so testers can open each person's magic link.
@Component({
  selector: 'app-dev-outbox',
  imports: [DatePipe],
  template: `
    <section class="outbox">
      <h1>Dev outbox</h1>
      <p class="muted">Emails the API would have sent. Open a link in a private window to act as that person.</p>
      @if (unavailable()) {
        <p class="alert">The outbox is only available when the API runs in Development.</p>
      }
      <ul class="outbox__list">
        @for (e of emails(); track $index) {
          <li class="outbox__item card">
            <strong>{{ e.to }}</strong>
            <span>{{ e.subject }}</span>
            <span class="muted">{{ e.sentAt | date: 'MMM d, h:mm:ss a' }}</span>
            @if (e.path) {
              <a [href]="e.path" target="_blank" rel="noopener">Open magic link ↗</a>
            }
          </li>
        } @empty {
          @if (!unavailable()) {
            <li class="muted">No emails yet.</li>
          }
        }
      </ul>
    </section>
  `,
  styles: `
    .outbox {
      max-width: 720px;
      margin: 0 auto;
      padding: var(--space-5) var(--space-4);
    }
    .outbox__list {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      margin: var(--space-4) 0 0;
      padding: 0;
      list-style: none;
    }
    .outbox__item {
      display: flex;
      flex-direction: column;
      gap: 2px;
      padding: var(--space-3) var(--space-4);
      overflow-wrap: anywhere;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DevOutbox implements OnInit {
  private readonly _trips = inject(TripService);

  protected readonly emails = signal<OutboxRow[]>([]);
  protected readonly unavailable = signal(false);

  async ngOnInit(): Promise<void> {
    try {
      const emails = await this._trips.outbox();
      // Links are opened on this origin so the dev proxy and cookies apply.
      this.emails.set(emails.map((e) => ({ ...e, path: /\/t\/[A-Za-z0-9_-]+/.exec(e.body)?.[0] ?? null })));
    } catch {
      this.unavailable.set(true);
    }
  }
}
