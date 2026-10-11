import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterRenderEffect,
  computed,
  effect,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { errorMessage } from '@functions/errors';
import { CATEGORY_ICON, formatMoney, formatPrice, formatTime, groupByDay, selectedOption, timeLeft } from '@functions/format';
import { ChangeRequest, ChatMessage, PlanItem, PlanOption, TripDetail } from '@interfaces/api';
import { MarkdownPipe } from '@pipes/markdown.pipe';
import { NotificationService } from '@services/notification.service';
import { TripService } from '@services/trip.service';
import { filter } from 'rxjs';

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

function activeNames(trip: TripDetail): string[] {
  const members = trip.members
    .filter((m) => m.status === 'Active')
    .sort((a, b) => (a.role === 'Primary' ? -1 : b.role === 'Primary' ? 1 : 0));
  return [...new Set(members.map((m) => shortName(m.email)))];
}

export interface PlanLine {
  id: string;
  icon: string;
  title: string;
  time: string | null;
  price: string | null;
}

// Continues the home page conversation for a created trip.
@Component({
  selector: 'app-trip',
  imports: [FormsModule, RouterLink, MarkdownPipe],
  templateUrl: './trip.html',
  styleUrl: './trip.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TripPage {
  private readonly _trips = inject(TripService);
  private readonly _notifications = inject(NotificationService);
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
  protected readonly busy = signal(false);
  private readonly _now = signal(Date.now());

  protected readonly summary = computed(() => {
    const t = this.trip();
    if (!t) {
      return null;
    }
    const names = activeNames(t);
    const who = names.length ? ` for ${joinNames(names)}` : '';
    return `Planning a ${t.destination} trip${who}.`;
  });

  protected readonly plan = computed(() => {
    const t = this.trip();
    if (!t?.items.length) {
      return null;
    }
    const days = groupByDay(t.items).map((g) => ({
      label: g.label,
      lines: g.items.map((item): PlanLine => {
        const option = selectedOption(item);
        return {
          id: item.id,
          icon: CATEGORY_ICON[item.category] ?? '',
          title: option?.title ?? item.title,
          time: option ? formatTime(option) : null,
          price: option ? formatPrice(option) : null,
        };
      }),
    }));
    const priced = t.items.map(selectedOption).filter((o): o is PlanOption => o?.estimatedCost != null);
    const currency = priced[0]?.currency ?? 'USD';
    const sum = priced.filter((o) => (o.currency ?? 'USD') === currency).reduce((acc, o) => acc + (o.estimatedCost ?? 0), 0);
    return { days, total: priced.length ? formatMoney(sum, currency) : null };
  });

  protected readonly isPrimary = computed(() => this.trip()?.myRole === 'Primary');
  protected readonly canSend = computed(() => {
    const t = this.trip();
    return !!t?.permissions.canSend && t.status === 'Planning' && t.items.length > 0;
  });
  protected readonly remaining = computed(() => timeLeft(this.trip()?.responseDeadline ?? null, this._now()));

  protected readonly sentNote = computed(() => {
    const t = this.trip();
    if (!t || !this.isPrimary() || t.status !== 'Agreement') {
      return null;
    }
    const others = activeNames(t).slice(1);
    const who = others.length ? joinNames(others) : 'the group';
    const left = this.remaining();
    return left ? `Sent to ${who}. They have ${left} left to answer.` : `Sent to ${who}. The response window has closed.`;
  });

  // Organizers change the plan by chatting, so only travelers get the poll.
  protected readonly questions = computed(() => {
    const t = this.trip();
    if (!t || this.isPrimary() || !t.permissions.canRespond) {
      return [];
    }
    return t.items.filter((i) => i.options.length > 0);
  });

  protected readonly pendingRequests = computed(() => {
    const t = this.trip();
    if (!t || !this.isPrimary() || t.status !== 'Agreement') {
      return [];
    }
    return t.changeRequests.filter((r) => r.status === 'Pending');
  });

  protected readonly myRequests = computed(() => {
    const t = this.trip();
    if (!t || this.isPrimary()) {
      return [];
    }
    return t.changeRequests.filter((r) => r.status !== 'Superseded');
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
      this.trip();
      window.scrollTo({ top: document.body.scrollHeight });
    });

    const timer = setInterval(() => this._now.set(Date.now()), 30_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));

    this._notifications.tripChanged$
      .pipe(
        filter((tripId) => tripId === this.id()),
        takeUntilDestroyed(),
      )
      .subscribe(() => void this.refresh());
  }

  private async refresh(): Promise<void> {
    try {
      const [trip, messages] = await Promise.all([this._trips.get(this.id()), this._trips.chat(this.id())]);
      this.trip.set(trip);
      this.messages.set(messages);
    } catch {
      // Keep showing the current state; the next change or reload will catch up.
    }
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
      if (response.planUpdated) {
        this.trip.set(await this._trips.get(this.id()));
      }
    } catch (e) {
      this.failed.set(text);
      this.error.set(errorMessage(e));
    } finally {
      this.pending.set(null);
      this._input()?.nativeElement.focus();
    }
  }

  protected readonly name = shortName;

  protected requestText(r: ChangeRequest): string {
    switch (r.status) {
      case 'Approved':
        return `Good news: ${r.toTitle} is in the plan now.`;
      case 'Denied':
        return `The organizer kept ${r.fromTitle} instead of ${r.toTitle}.`;
      default:
        return `I asked the organizer to swap ${r.fromTitle} for ${r.toTitle}. Waiting on their answer.`;
    }
  }

  protected chosenTitle(item: PlanItem): string | null {
    return item.options.find((o) => o.id === item.myChoiceOptionId)?.title ?? null;
  }

  protected sendToGroup(): Promise<void> {
    return this.act(() => this._trips.send(this.id()));
  }

  // The API treats missing items as accepting the current pick, so always send every answer.
  protected answer(item: PlanItem, optionId: string): Promise<void> {
    const t = this.trip();
    if (!t || item.myChoiceOptionId === optionId) {
      return Promise.resolve();
    }
    const choices = t.items
      .filter((i) => i.options.length > 0)
      .map((i) => ({
        itemId: i.id,
        optionId: i.id === item.id ? optionId : (i.myChoiceOptionId ?? i.selectedOptionId ?? i.options[0].id),
      }));
    return this.act(() => this._trips.respond(this.id(), choices));
  }

  protected decide(request: ChangeRequest, approve: boolean): Promise<void> {
    return this.act(() => this._trips.decideChange(this.id(), request.id, approve));
  }

  private async act(call: () => Promise<TripDetail>): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.failed.set(null);
    try {
      this.trip.set(await call());
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
