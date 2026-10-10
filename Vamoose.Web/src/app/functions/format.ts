import { PlanCategory, PlanItem, PlanOption } from '@interfaces/api';

export const CATEGORY_ICON: Record<PlanCategory, string> = {
  Flight: '✈️',
  Hotel: '🏨',
  Restaurant: '🍽️',
  Activity: '🎟️',
};

export function formatPrice(option: PlanOption): string | null {
  if (option.price == null) {
    return null;
  }
  try {
    return new Intl.NumberFormat(undefined, {
      style: 'currency',
      currency: option.currency ?? 'USD',
      maximumFractionDigits: 0,
    }).format(option.price);
  } catch {
    return `${option.price} ${option.currency ?? ''}`.trim();
  }
}

export function formatTime(option: PlanOption): string | null {
  if (!option.startTime) {
    return null;
  }
  return option.endTime ? `${option.startTime}–${option.endTime}` : option.startTime;
}

export function selectedOption(item: PlanItem): PlanOption | undefined {
  return item.options.find((o) => o.id === item.selectedOptionId) ?? item.options[0];
}

export interface DayGroup {
  label: string;
  items: PlanItem[];
}

export function groupByDay(items: PlanItem[]): DayGroup[] {
  const groups = new Map<string, PlanItem[]>();
  for (const item of items) {
    const label = item.day ? `Day ${item.day}` : 'Anytime';
    groups.set(label, [...(groups.get(label) ?? []), item]);
  }
  return [...groups.entries()].map(([label, groupItems]) => ({ label, items: groupItems }));
}
