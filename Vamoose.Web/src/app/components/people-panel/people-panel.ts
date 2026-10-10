import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Member, TripDetail } from '@interfaces/api';

@Component({
  selector: 'app-people-panel',
  imports: [FormsModule],
  templateUrl: './people-panel.html',
  styleUrl: './people-panel.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PeoplePanel {
  readonly trip = input.required<TripDetail>();
  readonly busy = input(false);
  readonly addMember = output<string>();

  protected readonly email = signal('');
  protected readonly active = computed(() => this.trip().members.filter((m) => m.status === 'Active'));
  protected readonly exited = computed(() => this.trip().members.filter((m) => m.status === 'Exited'));
  protected readonly showApprovals = computed(() => this.trip().status === 'Agreement' || this.trip().status === 'Locked');
  protected readonly approvedCount = computed(() => this.active().filter((m) => m.approval === 'Approved').length);

  protected initials(m: Member): string {
    return m.email.slice(0, 2).toUpperCase();
  }

  protected submit(): void {
    const email = this.email().trim();
    if (email) {
      this.addMember.emit(email);
      this.email.set('');
    }
  }

  protected readd(m: Member): void {
    this.addMember.emit(m.email);
  }
}
