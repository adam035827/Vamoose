import { PlanCategory, PlanItem, PlanOption } from '@interfaces/api';

export const CATEGORY_ICON: Record<PlanCategory, string> = {
  Flight: '✈️',
  Lodging: '🏨',
  Activity: '🎟️',
  Dining: '🍽️',
  Transit: '🚆',
  Free: '🌿',
};

export function formatMoney(amount: number, currency: string | null): string {
  try {
    return new Intl.NumberFormat(undefined, {
      style: 'currency',
      currency: currency ?? 'USD',
      maximumFractionDigits: 0,
    }).format(amount);
  } catch {
    return `${amount} ${currency ?? ''}`.trim();
  }
}

export function formatPrice(option: PlanOption): string | null {
  return option.estimatedCost == null ? null : formatMoney(option.estimatedCost, option.currency);
}

export function formatFetched(option: PlanOption): string | null {
  if (!option.priceFetchedAt) {
    return null;
  }
  const date = new Date(option.priceFetchedAt);
  return Number.isNaN(date.getTime())
    ? null
    : date.toLocaleDateString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
}

export function timeLeft(deadline: string | null, now: number): string | null {
  if (!deadline) {
    return null;
  }
  const ms = new Date(deadline).getTime() - now;
  if (ms <= 0) {
    return null;
  }
  const hours = Math.floor(ms / 3_600_000);
  const minutes = Math.floor((ms % 3_600_000) / 60_000);
  return hours > 0 ? `${hours}h ${minutes}m` : `${minutes}m`;
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
