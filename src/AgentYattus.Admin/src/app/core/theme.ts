import { Injectable, effect, signal } from '@angular/core';

export type ThemeChoice = 'light' | 'dark' | 'system';

/** Clair, sombre, ou comme le système. Le choix est retenu dans le navigateur (ce n'est pas un secret). */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly media = matchMedia('(prefers-color-scheme: dark)');
  private readonly systemDark = signal(this.media.matches);
  readonly choice = signal<ThemeChoice>(ThemeService.read());

  constructor() {
    this.media.addEventListener('change', (e) => this.systemDark.set(e.matches));
    effect(() => {
      const choice = this.choice();
      const dark = choice === 'dark' || (choice === 'system' && this.systemDark());
      document.documentElement.classList.toggle('dark', dark);
      try {
        localStorage.setItem('theme', choice);
      } catch {
        // Navigation privée : le choix ne sera pas retenu, rien de grave.
      }
    });
  }

  private static read(): ThemeChoice {
    try {
      const saved = localStorage.getItem('theme');
      return saved === 'light' || saved === 'dark' ? saved : 'system';
    } catch {
      return 'system';
    }
  }
}
