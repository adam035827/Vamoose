import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  protected readonly message = signal('Loading...');

  constructor() {
    inject(HttpClient)
      .get<{ message: string }>('/api/hello')
      .subscribe({
        next: r => this.message.set(r.message),
        error: () => this.message.set('Could not reach the API')
      });
  }
}
