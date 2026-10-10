import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { filter } from 'rxjs';
import { ChatPanel } from '@components/chat-panel/chat-panel';
import { PeoplePanel } from '@components/people-panel/people-panel';
import { PlanPanel } from '@components/plan-panel/plan-panel';
import { RequestsPanel } from '@components/requests-panel/requests-panel';
import { ReviewPanel } from '@components/review-panel/review-panel';
import { errorMessage } from '@functions/errors';
import { Choice, ExitPreview, TripDetail } from '@interfaces/api';
import { NotificationService } from '@services/notification.service';
import { TripService } from '@services/trip.service';

type Tab = 'chat' | 'plan' | 'review' | 'requests' | 'people';

// Wide screens always show the chat, so they open on the plan instead.
function defaultTab(): Tab {
  return window.matchMedia('(min-width: 960px)').matches ? 'plan' : 'chat';
}

@Component({
  selector: 'app-trip',
  imports: [RouterLink, DatePipe, ChatPanel, PlanPanel, ReviewPanel, RequestsPanel, PeoplePanel],
  templateUrl: './trip.html',
  styleUrl: './trip.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TripPage {
  private readonly _trips = inject(TripService);
  private readonly _router = inject(Router);

  readonly id = input.required<string>();

  protected readonly trip = signal<TripDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly gone = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly tab = signal<Tab>(defaultTab());
  protected readonly chatVersion = signal(0);
  protected readonly exitPreview = signal<ExitPreview | null>(null);

  protected readonly isPrimary = computed(() => this.trip()?.myRole === 'Primary');
  protected readonly pendingRequests = computed(
    () => this.trip()?.changeRequests.filter((r) => r.status === 'Pending').length ?? 0,
  );
  protected readonly tabs = computed<{ id: Tab; label: string; count?: number }[]>(() => {
    const t = this.trip();
    if (!t) {
      return [];
    }
    const tabs: { id: Tab; label: string; count?: number }[] = [
      { id: 'chat', label: 'Agent' },
      { id: 'plan', label: t.status === 'Locked' ? 'Final plan' : 'Plan' },
    ];
    if (t.status === 'Agreement' && t.items.length) {
      tabs.push({ id: 'review', label: 'Review', count: t.myApproval ? undefined : 1 });
    }
    if (this.isPrimary() && t.status !== 'Planning') {
      tabs.push({ id: 'requests', label: 'Requests', count: this.pendingRequests() || undefined });
    }
    tabs.push({ id: 'people', label: 'People' });
    return tabs;
  });

  protected readonly stage = computed(() => {
    const t = this.trip();
    if (!t) {
      return null;
    }
    const primary = this.isPrimary();
    switch (t.status) {
      case 'Planning':
        return {
          tone: 'info',
          text: t.items.length
            ? 'Fine-tune the plan with the agent, add your travelers, then finalize and send it.'
            : 'Tell the Vamoose agent about the trip to get a first plan with alternatives.',
        };
      case 'Agreement':
        if (t.consensus) {
          return {
            tone: 'success',
            text: primary ? 'Everyone approved the plan. You can lock the trip.' : 'Everyone approved the plan.',
          };
        }
        return {
          tone: 'warning',
          text: primary
            ? 'Waiting for the group to review the plan. You can still edit it, approve or deny change requests, or lock it.'
            : t.myApproval
              ? `You ${t.myApproval === 'Approved' ? 'approved' : 'asked for changes to'} this version of the plan.`
              : 'Review the plan and tell the group what works for you.',
        };
      case 'Locked':
        return { tone: 'success', text: 'This trip is locked. This is the final plan.' };
      case 'Cancelled':
        return {
          tone: 'danger',
          text: 'This trip is cancelled and read-only. Only you can see it, and you can reopen it.',
        };
    }
  });

  constructor() {
    effect(() => {
      const id = this.id();
      this.loading.set(true);
      this.gone.set(false);
      void this.load(id);
    });

    inject(NotificationService)
      .tripChanged$.pipe(
        filter((tripId) => tripId === this.id()),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe(() => {
        void this.load(this.id());
        this.chatVersion.update((v) => v + 1);
      });
  }

  protected async load(id: string): Promise<void> {
    try {
      const trip = await this._trips.get(id);
      this.trip.set(trip);
      this.error.set(null);
      if (!this.tabs().some((t) => t.id === this.tab())) {
        this.tab.set(defaultTab());
      }
    } catch (e) {
      if (e instanceof HttpErrorResponse && e.status === 404) {
        this.gone.set(true);
        this.trip.set(null);
      } else {
        this.error.set(errorMessage(e));
      }
    } finally {
      this.loading.set(false);
    }
  }

  protected selectTab(tab: Tab): void {
    this.tab.set(tab);
  }

  protected onPlanUpdated(): void {
    void this.load(this.id());
  }

  protected send(): Promise<void> {
    return this.run(() => this._trips.send(this.id()), 'review');
  }

  protected lock(): Promise<void> {
    return this.run(() => this._trips.lock(this.id()), 'plan');
  }

  protected unlock(): Promise<void> {
    return this.run(() => this._trips.unlock(this.id()));
  }

  protected reopen(): Promise<void> {
    return this.run(() => this._trips.reopen(this.id()));
  }

  protected select(event: { itemId: string; optionId: string }): Promise<void> {
    return this.run(() => this._trips.select(this.id(), event.itemId, event.optionId));
  }

  protected respond(choices: Choice[]): Promise<void> {
    return this.run(async () => {
      const trip = await this._trips.respond(this.id(), choices);
      this.chatVersion.update((v) => v + 1);
      return trip;
    });
  }

  protected decide(event: { requestId: string; approve: boolean }): Promise<void> {
    return this.run(() => this._trips.decideChange(this.id(), event.requestId, event.approve));
  }

  protected addMember(email: string): Promise<void> {
    return this.run(() => this._trips.addMember(this.id(), email));
  }

  protected async startExit(): Promise<void> {
    this.busy.set(true);
    try {
      this.exitPreview.set(await this._trips.exitPreview(this.id()));
      this.chatVersion.update((v) => v + 1);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected stay(): void {
    this.exitPreview.set(null);
    this.tab.set(defaultTab() === 'chat' ? 'chat' : this.tab());
  }

  protected async confirmExit(): Promise<void> {
    const preview = this.exitPreview();
    this.busy.set(true);
    try {
      await this._trips.exit(this.id());
      this.exitPreview.set(null);
      if (preview?.cancelsTrip) {
        await this.load(this.id());
      } else {
        await this._router.navigate(['/']);
      }
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  private async run(action: () => Promise<TripDetail>, nextTab?: Tab): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      this.trip.set(await action());
      if (nextTab && this.tabs().some((t) => t.id === nextTab)) {
        this.tab.set(nextTab);
      }
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
