import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterRenderEffect,
  computed,
  effect,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { errorMessage } from '@functions/errors';
import { ChatMessage, TripDetail } from '@interfaces/api';
import { TripService } from '@services/trip.service';

// "robert.smith@x.com" -> "Robert"
function shortName(email: string): string {
  const first = email.split('@')[0].split(/[._\-+]/)[0] || email;
  return first.charAt(0).toUpperCase() + first.slice(1);
}

function joinNames(names: string[]): string {
  if (names.length <= 2) {
    return names.join(' and ');
  }
  return `${names.slice(0, -1).join(', ')}, and ${names[names.length - 1]}`;
}

// Continues the home page conversation for a created trip.
@Component({
  selector: 'app-trip',
  imports: [FormsModule, RouterLink],
  templateUrl: './trip.html',
  styleUrl: './trip.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TripPage {
  private readonly _trips = inject(TripService);
  private readonly _input = viewChild<ElementRef<HTMLInputElement>>('messageInput');

  readonly id = input.required<string>();

  protected readonly messages = signal<ChatMessage[]>([]);
  protected readonly trip = signal<TripDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly gone = signal(false);
  protected readonly draft = signal('');
  protected readonly pending = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly failed = signal<string | null>(null);

  protected readonly summary = computed(() => {
    const t = this.trip();
    if (!t) {
      return null;
    }
    const members = [...t.members]
      .filter((m) => m.status === 'Active')
      .sort((a, b) => (a.role === 'Primary' ? -1 : b.role === 'Primary' ? 1 : 0));
    const names = [...new Set(members.map((m) => shortName(m.email)))];
    const who = names.length ? ` for ${joinNames(names)}` : '';
    return `Planning a ${t.destination} trip${who}.`;
  });

  // The summary follows the opening prompt from the home page.
  protected readonly conversation = computed<ChatMessage[]>(() => {
    const list = this.messages();
    const text = this.summary();
    if (!text) {
      return list;
    }
    const at = list[0]?.role === 'User' ? 1 : 0;
    const summary: ChatMessage = { id: 'summary', role: 'Agent', content: text, createdAt: list[0]?.createdAt ?? '' };
    return [...list.slice(0, at), summary, ...list.slice(at)];
  });
  constructor() {
    effect(() => void this.load(this.id()));

    afterRenderEffect(() => {
      this.messages();
      this.pending();
      window.scrollTo({ top: document.body.scrollHeight });
    });
  }

  private async load(id: string): Promise<void> {
    this.loading.set(true);
    this.gone.set(false);
    this.error.set(null);
    try {
      const [trip, messages] = await Promise.all([this._trips.get(id), this._trips.chat(id)]);
      this.trip.set(trip);
      this.messages.set(messages);
    } catch (e) {
      if (e instanceof HttpErrorResponse && e.status === 404) {
        this.gone.set(true);
      } else {
        this.error.set(errorMessage(e));
      }
    } finally {
      this.loading.set(false);
    }
  }

  protected retry(): void {
    const text = this.failed();
    if (text) {
      this.draft.set(text);
      void this.send();
    }
  }

  protected async send(): Promise<void> {
    const text = this.draft().trim();
    if (!text || this.pending()) {
      return;
    }
    this.pending.set(text);
    this.error.set(null);
    this.failed.set(null);
    this.draft.set('');
    try {
      const response = await this._trips.sendMessage(this.id(), text);
      this.messages.update((list) => [...list, ...response.messages]);
    } catch (e) {
      this.failed.set(text);
      this.error.set(errorMessage(e));
    } finally {
      this.pending.set(null);
      this._input()?.nativeElement.focus();
    }
  }
}
