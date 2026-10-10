import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { errorMessage } from '@functions/errors';
import { TripService } from '@services/trip.service';

@Component({
  selector: 'app-trip-new',
  imports: [FormsModule, RouterLink],
  templateUrl: './trip-new.html',
  styleUrl: './trip-new.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TripNew {
  private readonly _trips = inject(TripService);
  private readonly _router = inject(Router);

  protected readonly name = signal('');
  protected readonly destination = signal('');
  protected readonly startDate = signal('');
  protected readonly endDate = signal('');
  protected readonly notes = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly datesInvalid = computed(
    () => !!this.startDate() && !!this.endDate() && this.endDate() < this.startDate(),
  );
  protected readonly canSubmit = computed(
    () => !!this.name().trim() && !!this.destination().trim() && !this.datesInvalid() && !this.busy(),
  );

  protected async submit(): Promise<void> {
    if (!this.canSubmit()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      const trip = await this._trips.create({
        name: this.name().trim(),
        destination: this.destination().trim(),
        startDate: this.startDate() || null,
        endDate: this.endDate() || null,
        notes: this.notes().trim() || null,
      });
      await this._router.navigate(['/trips', trip.id]);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
