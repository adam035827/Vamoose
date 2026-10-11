import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnInit,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { errorMessage } from '@functions/errors';
import { StartIntro, TripSummary } from '@interfaces/api';
import { AuthService } from '@services/auth.service';
import { TripService } from '@services/trip.service';

type Step = 'ask' | 'thinking' | 'who' | 'starting';

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

// The home page is the agent (requirements 3.1): where to go, then who you are and who's going.
@Component({
  selector: 'app-home',
  imports: [FormsModule],
  templateUrl: './home.html',
  styleUrl: './home.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Home implements OnInit {
  private readonly _promptInput = viewChild<ElementRef<HTMLInputElement>>('promptInput');

  private readonly _trips = inject(TripService);
  private readonly _auth = inject(AuthService);
  private readonly _router = inject(Router);

  protected readonly step = signal<Step>('ask');
  protected readonly prompt = signal('');
  protected readonly intro = signal<StartIntro | null>(null);
  protected readonly organizerEmail = signal('');
  protected readonly travelerInput = signal('');
  protected readonly error = signal<string | null>(null);
  protected readonly trips = signal<TripSummary[]>([]);
  protected readonly user = this._auth.user;

  constructor() {
    afterNextRender(() => this._promptInput()?.nativeElement.focus());
  }

  protected readonly travelers = computed(() =>
    [...new Set(this.travelerInput().split(/[\s,;]+/).map((e) => e.trim().toLowerCase()).filter(Boolean))],
  );
  protected readonly invalidTravelers = computed(() => this.travelers().filter((e) => !EMAIL.test(e)));
  protected readonly organizerValid = computed(() => EMAIL.test(this.organizerEmail().trim()));
  protected readonly canStart = computed(
    () => this.organizerValid() && this.invalidTravelers().length === 0 && this.step() === 'who',
  );

  async ngOnInit(): Promise<void> {
    if (await this._auth.ensureSession()) {
      this.organizerEmail.set(this._auth.user()?.email ?? '');
      try {
        this.trips.set(await this._trips.list());
      } catch {
        // The start flow works without the list.
      }
    }
  }

  protected async ask(): Promise<void> {
    const text = this.prompt().trim();
    if (!text || this.step() === 'thinking') {
      return;
    }
    this.step.set('thinking');
    this.error.set(null);
    try {
      this.intro.set(await this._trips.intro(text));
      this.step.set('who');
    } catch (e) {
      this.error.set(errorMessage(e));
      this.step.set('ask');
    }
  }

  protected async start(): Promise<void> {
    const intro = this.intro();
    if (!intro || !this.canStart()) {
      return;
    }
    this.step.set('starting');
    this.error.set(null);
    try {
      const result = await this._trips.start({
        prompt: this.prompt().trim(),
        destination: intro.destination,
        tripName: intro.tripName,
        organizerEmail: this.organizerEmail().trim(),
        travelerEmails: this.travelers(),
      });
      this._auth.setUser(result.user);
      await this._router.navigate(['/trips', result.tripId]);
    } catch (e) {
      this.error.set(errorMessage(e));
      this.step.set('who');
    }
  }

  protected startOver(): void {
    this.step.set('ask');
    this.intro.set(null);
    this.error.set(null);
  }
}
