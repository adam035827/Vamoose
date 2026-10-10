import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { AppNotification } from '@interfaces/api';
import { AuthService } from '@services/auth.service';
import { NotificationService } from '@services/notification.service';
import { NotificationBell } from '@components/notification-bell/notification-bell';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, NotificationBell],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  private readonly _auth = inject(AuthService);
  private readonly _notifications = inject(NotificationService);
  private readonly _router = inject(Router);

  protected readonly user = this._auth.user;
  protected readonly toast = signal<AppNotification | null>(null);
  private _toastTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      if (this._auth.isLoggedIn()) {
        void this._notifications.start();
      } else {
        void this._notifications.stop();
      }
    });

    this._notifications.incoming$.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe((n) => {
      this.toast.set(n);
      clearTimeout(this._toastTimer);
      this._toastTimer = setTimeout(() => this.toast.set(null), 6000);
    });
  }

  protected async logout(): Promise<void> {
    await this._auth.logout();
    await this._router.navigate(['/login']);
  }

  protected openToast(n: AppNotification): void {
    this.toast.set(null);
    void this._notifications.markRead(n.id);
    if (n.tripId) {
      void this._router.navigate(['/trips', n.tripId]);
    }
  }
}
