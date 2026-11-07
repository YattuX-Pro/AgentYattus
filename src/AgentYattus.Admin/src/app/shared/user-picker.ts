import { Component, inject, input, output, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUserPlus } from '@ng-icons/lucide';
import { AdminApi, UserRef } from '../core/api';

/**
 * Chercher une personne de l'annuaire pour l'inviter. Seules les personnes déjà connectées une fois apparaissent :
 * les comptes se créent dans Keycloak (ADR 0009).
 */
@Component({
  selector: 'app-user-picker',
  imports: [NgIcon],
  providers: [provideIcons({ lucideUserPlus })],
  host: { class: 'relative block' },
  template: `
    <label class="bg-muted text-muted-foreground focus-within:ring-ring/40 focus-within:bg-background flex h-8 items-center gap-2 rounded-md border px-2.5 focus-within:ring-[3px]">
      <ng-icon name="lucideUserPlus" />
      <input
        class="text-foreground min-w-0 flex-1 bg-transparent outline-none"
        [placeholder]="placeholder()"
        aria-label="Chercher une personne"
        [value]="query()"
        (input)="search($any($event.target).value)"
        (keydown.escape)="results.set([])"
      />
    </label>
    @if (results().length) {
      <div class="bg-popover animate-in fade-in-0 zoom-in-95 slide-in-from-top-1 absolute inset-x-0 top-9 z-10 flex flex-col rounded-md border p-1 shadow-md duration-150" role="listbox">
        @for (u of results(); track u.id) {
          <button type="button" role="option" class="hover:bg-accent flex items-center justify-between gap-2 rounded px-2 py-1.5 text-left" (click)="pick(u)">
            <span class="font-medium">{{ u.displayName ?? u.username }}</span>
            <span class="text-muted-foreground font-mono text-xs">{{ u.username }}</span>
          </button>
        }
      </div>
    } @else if (searched() && query().length >= 2) {
      <p class="text-muted-foreground animate-in fade-in-0 mt-1 text-xs">Personne trouvée. Le compte doit exister dans Keycloak et s'être connecté une fois.</p>
    }
  `,
})
export class UserPicker {
  private readonly api = inject(AdminApi);
  readonly placeholder = input('Nom ou identifiant');
  readonly picked = output<UserRef>();

  protected readonly query = signal('');
  protected readonly results = signal<UserRef[]>([]);
  protected readonly searched = signal(false);
  private timer: ReturnType<typeof setTimeout> | undefined;

  protected search(value: string) {
    this.query.set(value);
    this.searched.set(false);
    clearTimeout(this.timer);
    if (value.trim().length < 2) {
      this.results.set([]);
      return;
    }
    // Attendre que la frappe s'arrête : une requête par recherche, pas par lettre.
    this.timer = setTimeout(async () => {
      try {
        this.results.set(await this.api.lookup(value.trim()));
      } catch {
        this.results.set([]);
      }
      this.searched.set(true);
    }, 250);
  }

  protected pick(user: UserRef) {
    this.picked.emit(user);
    this.query.set('');
    this.results.set([]);
    this.searched.set(false);
  }
}
