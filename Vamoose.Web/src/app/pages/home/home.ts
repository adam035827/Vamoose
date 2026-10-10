import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { errorMessage } from '@functions/errors';
import { TripSummary } from '@interfaces/api';
import { NotificationService } from '@services/notification.service';
import { TripService } from '@services/trip.service';

@Component({
  selector: 'app-home',
  imports: [RouterLink, DatePipe, NgTemplateOutlet],
  templateUrl: './home.html',
  styleUrl: './home.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Home implements OnInit {
  private readonly _trips = inject(TripService);

  protected readonly trips = signal<TripSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly organizing = computed(() => this.trips().filter((t) => t.myRole === 'Primary'));
  protected readonly invited = computed(() => this.trips().filter((t) => t.myRole === 'Traveler'));

  constructor() {
    inject(NotificationService)
      .incoming$.pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe(() => void this.load());
  }

  ngOnInit(): void {
    void this.load();
  }

  protected async load(): Promise<void> {
    try {
      this.trips.set(await this._trips.list());
      this.error.set(null);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  protected statusLabel(t: TripSummary): string {
    switch (t.status) {
      case 'Planning':
        return 'Planning';
      case 'Agreement':
        return 'Waiting on the group';
      case 'Locked':
        return 'Locked';
      case 'Cancelled':
        return 'Cancelled';
    }
  }
}
