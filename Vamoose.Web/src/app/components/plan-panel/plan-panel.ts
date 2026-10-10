import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { CATEGORY_ICON, formatFetched, formatMoney, formatPrice, formatTime, groupByDay, selectedOption } from '@functions/format';
import { PlanItem, PlanOption, TripDetail } from '@interfaces/api';

@Component({
  selector: 'app-plan-panel',
  templateUrl: './plan-panel.html',
  styleUrl: './plan-panel.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlanPanel {
  readonly trip = input.required<TripDetail>();
  readonly busy = input(false);
  readonly selectOption = output<{ itemId: string; optionId: string }>();

  protected readonly expanded = signal<Set<string>>(new Set());
  protected readonly groups = computed(() => groupByDay(this.trip().items));
  protected readonly canEdit = computed(() => this.trip().permissions.canEditPlan);
  protected readonly showVotes = computed(() => this.trip().status === 'Agreement' || this.trip().status === 'Locked');
  protected readonly isFinal = computed(() => this.trip().status === 'Locked');
  protected readonly total = computed(() => {
    const options = this.trip().items.map(selectedOption).filter((o): o is PlanOption => o?.estimatedCost != null);
    if (!options.length) {
      return null;
    }
    const currency = options[0].currency ?? 'USD';
    const sum = options.filter((o) => (o.currency ?? 'USD') === currency).reduce((acc, o) => acc + (o.estimatedCost ?? 0), 0);
    return formatMoney(sum, currency);
  });

  protected readonly icon = CATEGORY_ICON;
  protected readonly price = formatPrice;
  protected readonly time = formatTime;
  protected readonly fetched = formatFetched;
  protected readonly selected = selectedOption;

  protected alternatives(item: PlanItem): PlanOption[] {
    return item.options.filter((o) => o.id !== item.selectedOptionId);
  }

  protected isExpanded(item: PlanItem): boolean {
    return this.expanded().has(item.id);
  }

  protected toggle(item: PlanItem): void {
    this.expanded.update((set) => {
      const next = new Set(set);
      if (next.has(item.id)) {
        next.delete(item.id);
      } else {
        next.add(item.id);
      }
      return next;
    });
  }

  protected use(item: PlanItem, option: PlanOption): void {
    this.selectOption.emit({ itemId: item.id, optionId: option.id });
  }
}
