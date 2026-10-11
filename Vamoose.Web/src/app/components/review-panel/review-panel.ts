import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { CATEGORY_ICON, formatPrice, formatTime, timeLeft } from '@functions/format';
import { Choice, PlanItem, TripDetail } from '@interfaces/api';

@Component({
  selector: 'app-review-panel',
  templateUrl: './review-panel.html',
  styleUrl: './review-panel.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReviewPanel {
  readonly trip = input.required<TripDetail>();
  readonly busy = input(false);
  readonly submitChoices = output<Choice[]>();
  readonly openChat = output<void>();

  protected readonly choices = signal<Record<string, string>>({});
  protected readonly icon = CATEGORY_ICON;
  protected readonly price = formatPrice;
  protected readonly time = formatTime;

  protected readonly changes = computed(() =>
    this.trip().items.filter((i) => this.choices()[i.id] && this.choices()[i.id] !== i.selectedOptionId),
  );

  // Travelers have 24 hours to respond to each version of the plan.
  private readonly _now = signal(Date.now());
  protected readonly remaining = computed(() => timeLeft(this.trip().responseDeadline, this._now()));

  constructor() {
    const timer = setInterval(() => this._now.set(Date.now()), 30_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));

    // Start from the user's earlier answers, or the plan's current choice.
    effect(() => {
      const trip = this.trip();
      const next: Record<string, string> = {};
      for (const item of trip.items) {
        next[item.id] = item.myChoiceOptionId ?? item.selectedOptionId ?? item.options[0]?.id;
      }
      this.choices.set(next);
    });
  }

  protected choose(item: PlanItem, optionId: string): void {
    this.choices.update((c) => ({ ...c, [item.id]: optionId }));
  }

  protected submit(): void {
    this.submitChoices.emit(
      Object.entries(this.choices()).map(([itemId, optionId]) => ({ itemId, optionId })),
    );
  }
}
