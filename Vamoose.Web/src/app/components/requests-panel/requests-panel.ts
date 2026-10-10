import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { TripDetail } from '@interfaces/api';

@Component({
  selector: 'app-requests-panel',
  imports: [DatePipe],
  templateUrl: './requests-panel.html',
  styleUrl: './requests-panel.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RequestsPanel {
  readonly trip = input.required<TripDetail>();
  readonly busy = input(false);
  readonly decide = output<{ requestId: string; approve: boolean }>();

  protected readonly pending = computed(() => this.trip().changeRequests.filter((r) => r.status === 'Pending'));
  protected readonly history = computed(() => this.trip().changeRequests.filter((r) => r.status !== 'Pending'));
  protected readonly canDecide = computed(() => this.trip().status === 'Agreement');
}
