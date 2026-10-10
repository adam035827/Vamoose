import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { User } from '@interfaces/api';

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

  async login(email: string): Promise<void> {
    this._user.set(await firstValueFrom(this._http.post<User>('/api/auth/login', { email })));
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
