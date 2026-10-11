import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterRenderEffect,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorMessage } from '@functions/errors';
import { ChatMessage } from '@interfaces/api';
import { MarkdownPipe } from '@pipes/markdown.pipe';
import { TripService } from '@services/trip.service';

const PLANNING_PROMPTS = [
  'Plan a 4-day trip for 4 friends, mid-range budget',
  'Find flights from New York',
  'Suggest cheaper hotels',
  'Add a great dinner spot for night 2',
];
const TRAVELER_PROMPTS = ['What does the plan include?', 'How much will it cost per person?', 'Why this hotel?'];

@Component({
  selector: 'app-chat-panel',
  imports: [FormsModule, DatePipe, MarkdownPipe],
  templateUrl: './chat-panel.html',
  styleUrl: './chat-panel.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChatPanel {
  private readonly _trips = inject(TripService);
  private readonly _scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  readonly tripId = input.required<string>();
  readonly canChat = input(false);
  readonly canEdit = input(false);
  // Bumped by the parent when something outside the chat (nudges, retention) may have added messages.
  readonly version = input(0);
  readonly planUpdated = output<void>();

  protected readonly messages = signal<ChatMessage[]>([]);
  protected readonly draft = signal('');
  protected readonly sending = signal(false);
  protected readonly pending = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly failed = signal<string | null>(null);

  protected get prompts(): string[] {
    return this.canEdit() ? PLANNING_PROMPTS : TRAVELER_PROMPTS;
  }

  constructor() {
    effect(() => {
      this.version();
      void this.load(this.tripId());
    });

    afterRenderEffect(() => {
      this.messages();
      this.pending();
      const el = this._scroller()?.nativeElement;
      if (el) {
        el.scrollTop = el.scrollHeight;
      }
    });
  }

  protected async load(tripId: string): Promise<void> {
    try {
      this.messages.set(await this._trips.chat(tripId));
    } catch (e) {
      this.error.set(errorMessage(e));
    }
  }

  protected usePrompt(prompt: string): void {
    this.draft.set(prompt);
    void this.send();
  }

  protected retry(): void {
    const text = this.failed();
    if (text) {
      this.draft.set(text);
      void this.send();
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.send();
    }
  }

  protected async send(): Promise<void> {
    const text = this.draft().trim();
    if (!text || this.sending() || !this.canChat()) {
      return;
    }
    this.sending.set(true);
    this.pending.set(text);
    this.error.set(null);
    this.failed.set(null);
    this.draft.set('');
    try {
      const response = await this._trips.sendMessage(this.tripId(), text);
      this.messages.update((list) => [...list, ...response.messages]);
      if (response.planUpdated) {
        this.planUpdated.emit();
      }
    } catch (e) {
      this.failed.set(text);
      this.error.set(errorMessage(e));
    } finally {
      this.pending.set(null);
      this.sending.set(false);
    }
  }
}
