import { Component, DestroyRef, inject } from '@angular/core';
import { Router } from '@angular/router';

@Component({
  selector: 'app-welcome',
  imports: [],
  templateUrl: './welcome.html',
  styleUrl: './welcome.css',
})
export class Welcome {
  private readonly _destroyRef = inject(DestroyRef);
  private readonly _router = inject(Router);
  private _navigationTimeout: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    this._destroyRef.onDestroy(() => {
      if (this._navigationTimeout !== undefined) {
        clearTimeout(this._navigationTimeout);
      }
    });
  }

  onAccentAnimationEnd(): void {
    if (this._navigationTimeout !== undefined) {
      return;
    }

    this._navigationTimeout = setTimeout(() => {
      void this._router.navigateByUrl('/home');
    }, 1500);
  }
}
