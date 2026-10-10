import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { Subject, firstValueFrom } from 'rxjs';
import { AppNotification } from '@interfaces/api';

// Live notifications over SignalR, plus the stored list for anything missed while offline.
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly _http = inject(HttpClient);
  private readonly _items = signal<AppNotification[]>([]);
  private readonly _tripChanged = new Subject<string>();
  private readonly _incoming = new Subject<AppNotification>();
  private _hub: HubConnection | null = null;

  readonly items = this._items.asReadonly();
  readonly unread = computed(() => this._items().filter((n) => !n.read).length);
  readonly tripChanged$ = this._tripChanged.asObservable();
  readonly incoming$ = this._incoming.asObservable();

  async start(): Promise<void> {
    await this.refresh();
    if (this._hub) {
      return;
    }

    this._hub = new HubConnectionBuilder()
      .withUrl('/hubs/notifications')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    this._hub.on('notification', (n: AppNotification) => {
      this._items.update((list) => [n, ...list.filter((x) => x.id !== n.id)]);
      this._incoming.next(n);
    });
    this._hub.on('tripChanged', (tripId: string) => this._tripChanged.next(tripId));
    this._hub.onreconnected(() => void this.refresh());
    try {
      await this._hub.start();
    } catch {
      // The list still works without live updates; reconnects are retried on the next start().
      this._hub = null;
    }
  }

  async stop(): Promise<void> {
    await this._hub?.stop();
    this._hub = null;
    this._items.set([]);
  }

  async refresh(): Promise<void> {
    try {
      this._items.set(await firstValueFrom(this._http.get<AppNotification[]>('/api/notifications')));
    } catch {
      // Keep the current list if the refresh fails.
    }
  }

  async markRead(id: string): Promise<void> {
    this._items.update((list) => list.map((n) => (n.id === id ? { ...n, read: true } : n)));
    await firstValueFrom(this._http.post(`/api/notifications/${id}/read`, {}));
  }

  async markAllRead(): Promise<void> {
    this._items.update((list) => list.map((n) => ({ ...n, read: true })));
    await firstValueFrom(this._http.post('/api/notifications/read-all', {}));
  }
}
