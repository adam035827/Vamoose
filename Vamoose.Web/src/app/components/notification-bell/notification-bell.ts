import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, ElementRef, HostListener, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AppNotification } from '@interfaces/api';
import { NotificationService } from '@services/notification.service';

@Component({
  selector: 'app-notification-bell',
  imports: [DatePipe],
  templateUrl: './notification-bell.html',
  styleUrl: './notification-bell.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotificationBell {
  private readonly _notifications = inject(NotificationService);
  private readonly _router = inject(Router);
  private readonly _host = inject(ElementRef<HTMLElement>);

  protected readonly items = this._notifications.items;
  protected readonly unread = this._notifications.unread;
  protected readonly open = signal(false);

  @HostListener('document:click', ['$event'])
  protected onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this._host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }

  protected toggle(): void {
    this.open.update((v) => !v);
  }

  protected select(n: AppNotification): void {
    this.open.set(false);
    void this._notifications.markRead(n.id);
    if (n.tripId) {
      void this._router.navigate(['/trips', n.tripId]);
    }
  }

  protected markAll(): void {
    void this._notifications.markAllRead();
  }
}
