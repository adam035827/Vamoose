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
  private readonly _firstQuestion = viewChild<ElementRef<HTMLElement>>('firstQuestion');
  private readonly _convo = viewChild<ElementRef<HTMLElement>>('convo');
  private readonly _seen = new WeakSet<Element>();
  private _initialScrollDone = false;
  private readonly _loggedLinks = new Set<string>();

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

  // Travelers can open their magic link before the organizer sends the plan.
  protected readonly waitingNote = computed(() => {
    const t = this.trip();
    if (!t || this.isPrimary() || t.status !== 'Planning') {
      return null;
    }
    return `${shortName(t.primaryEmail)} is still planning this trip. You'll see the questions here as soon as it's sent.`;
  });
  protected readonly canSend = computed(() => {
    const t = this.trip();
    return !!t?.permissions.canSend && t.status === 'Planning' && t.items.length > 0 && !t.researchPending;
  });
  protected readonly researching = computed(() => this.isPrimary() && !!this.trip()?.researchPending);
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

  // Answers held locally until every question is answered, then submitted together.
  private readonly _answers = signal<Record<string, string>>({});

  // Questions revealed so far: every answered one plus the first unanswered.
  protected readonly steps = computed(() => {
    const answers = this._answers();
    const steps: { item: PlanItem; chosen: string | null }[] = [];
    for (const item of this.questions()) {
      const chosenId = item.myChoiceOptionId ?? answers[item.id] ?? null;
      steps.push({ item, chosen: item.options.find((o) => o.id === chosenId)?.title ?? null });
      if (!chosenId) {
        break;
      }
    }
    return steps;
  });

  protected readonly submitting = computed(
    () => this.busy() && this.questions().length > 0 && this.questions().every((i) => !i.myChoiceOptionId && this._answers()[i.id]),
  );

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
      this.steps();
      const convo = this._convo()?.nativeElement;
      if (!convo || this.loading()) {
        return;
      }
      const items = Array.from(convo.children).filter((el) => !el.classList.contains('page__status'));
      const added = items.filter((el) => !this._seen.has(el));
      added.forEach((el) => this._seen.add(el));

      if (!this._initialScrollDone) {
        this._initialScrollDone = true;
        // Travelers arriving from their magic link land on the questions first.
        const first = this._firstQuestion()?.nativeElement;
        if (first) {
          first.scrollIntoView({ block: 'start' });
        } else {
          window.scrollTo({ top: document.body.scrollHeight });
        }
        return;
      }
      added[0]?.scrollIntoView({ block: 'start', behavior: 'smooth' });
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
      void this.logMagicLinks(trip);
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
      void this.logMagicLinks(trip);
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

  // No email provider yet: in development the organizer's browser console shows everyone's magic link.
  private async logMagicLinks(trip: TripDetail): Promise<void> {
    if (trip.myRole !== 'Primary') {
      return;
    }
    try {
      const links = (await this._trips.magicLinks(trip.id)).filter((l) => !this._loggedLinks.has(l.link));
      if (!links.length) {
        return;
      }
      links.forEach((l) => this._loggedLinks.add(l.link));
      console.info(`[Vamoose] Magic links for "${trip.name}":`);
      links.forEach((l) => console.info(`[Vamoose] ${l.email} (${l.role}): ${l.link}`));
    } catch {
      // Not exposed outside development.
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

  protected sendToGroup(): Promise<void> {
    return this.act(() => this._trips.send(this.id()));
  }

  protected async answer(item: PlanItem, optionId: string): Promise<void> {
    if (this.busy()) {
      return;
    }
    this._answers.update((a) => ({ ...a, [item.id]: optionId }));
    const answers = this._answers();
    const questions = this.questions();
    if (!questions.every((i) => i.myChoiceOptionId ?? answers[i.id])) {
      return;
    }
    const choices = questions.map((i) => ({ itemId: i.id, optionId: answers[i.id] ?? i.myChoiceOptionId ?? '' }));
    await this.act(() => this._trips.respond(this.id(), choices));
    // On failure, reopen the last question so the traveler can resubmit.
    if (this.error()) {
      this._answers.update((a) => {
        const rest = { ...a };
        delete rest[item.id];
        return rest;
      });
    }
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
