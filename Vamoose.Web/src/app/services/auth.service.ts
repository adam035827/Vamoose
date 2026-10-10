import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MagicLinkResult, User } from '@interfaces/api';

// There is no login: users are signed in by their trip's magic link or by starting a trip (requirements 3.2).
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly _http = inject(HttpClient);
  private readonly _user = signal<User | null>(null);
  private _checked = false;

  readonly user = this._user.asReadonly();
  readonly isLoggedIn = computed(() => this._user() !== null);

  // Restores the session from the auth cookie once per page load.
  async ensureSession(): Promise<boolean> {
    if (!this._checked) {
      this._checked = true;
      try {
        this._user.set(await firstValueFrom(this._http.get<User>('/api/auth/me')));
      } catch {
        this._user.set(null);
      }
    }
    return this.isLoggedIn();
  }

  async openMagicLink(token: string): Promise<MagicLinkResult> {
    const result = await firstValueFrom(this._http.post<MagicLinkResult>('/api/auth/magic', { token }));
    this.setUser(result.user);
    return result;
  }

  setUser(user: User): void {
    this._user.set(user);
    this._checked = true;
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this._http.post('/api/auth/logout', {}));
    } finally {
      this.clear();
    }
  }

  clear(): void {
    this._user.set(null);
  }
}
