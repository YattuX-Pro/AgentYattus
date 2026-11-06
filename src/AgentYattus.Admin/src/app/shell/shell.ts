import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideActivity, lucideBot, lucideBuilding2, lucideChevronDown, lucideCpu, lucideFolderKanban, lucideLayoutDashboard, lucideLogOut,
  lucideMenu, lucideMonitor, lucideMoon, lucideScrollText, lucideSearch, lucideSun, lucideUsers,
} from '@ng-icons/lucide';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCollapsibleImports } from '@spartan-ng/helm/collapsible';
import { HlmKbdImports } from '@spartan-ng/helm/kbd';
import { HlmToasterImports } from '@spartan-ng/helm/sonner';
import { HlmToggleGroupImports } from '@spartan-ng/helm/toggle-group';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { filter, map } from 'rxjs';
import { AuthService } from '../core/auth';
import { ThemeChoice, ThemeService } from '../core/theme';
import { NAV, locate } from './nav';

@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive, NgIcon, HlmButtonImports, HlmCollapsibleImports,
    HlmAvatarImports, HlmKbdImports, HlmToggleGroupImports, HlmTooltipImports, HlmToasterImports,
  ],
  providers: [
    provideIcons({
      lucideActivity, lucideBot, lucideBuilding2, lucideChevronDown, lucideCpu, lucideFolderKanban, lucideLayoutDashboard, lucideLogOut,
      lucideMenu, lucideMonitor, lucideMoon, lucideScrollText, lucideSearch, lucideSun, lucideUsers,
    }),
  ],
  host: { '(document:keydown)': 'onKey($event)' },
  templateUrl: './shell.html',
})
export class Shell {
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(ThemeService);
  private readonly router = inject(Router);

  protected readonly nav = NAV;
  protected readonly menuOpen = signal(false);
  protected readonly open = signal<Record<string, boolean>>(Shell.readGroups());
  private readonly url = toSignal(
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd), map((e) => e.urlAfterRedirects)),
    { initialValue: this.router.url },
  );
  protected readonly here = computed(() => locate(this.url()));
  protected readonly initials = computed(() => this.auth.name().slice(0, 2).toUpperCase());

  protected setGroup(label: string, expanded: boolean) {
    this.open.update((o) => ({ ...o, [label]: expanded }));
    try {
      localStorage.setItem('navGroups', JSON.stringify(this.open()));
    } catch {
      // Navigation privée : les groupes seront rouverts au prochain chargement.
    }
  }

  protected setTheme(value: unknown) {
    if (value === 'light' || value === 'dark' || value === 'system') this.theme.choice.set(value as ThemeChoice);
  }

  protected search(query: string) {
    void this.router.navigate(['/missions'], { queryParams: query.trim() ? { q: query.trim() } : {} });
  }

  protected onKey(event: KeyboardEvent) {
    if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      document.getElementById('search')?.focus();
    }
    if (event.key === 'Escape') this.menuOpen.set(false);
  }

  private static readGroups(): Record<string, boolean> {
    const all = Object.fromEntries(NAV.map((g) => [g.label, true]));
    try {
      return { ...all, ...JSON.parse(localStorage.getItem('navGroups') ?? '{}') };
    } catch {
      return all;
    }
  }
}
