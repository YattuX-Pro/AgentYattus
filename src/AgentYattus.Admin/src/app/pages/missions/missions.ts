import { Component, DestroyRef, ElementRef, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronRight, lucideRefreshCw, lucideSearch } from '@ng-icons/lucide';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { AdminApi, AuditEntry, Mission, errorMessage } from '../../core/api';
import { STATUS, argumentsOf, describe } from '../../core/events';
import { when } from '../../core/format';
import { PageHeader } from '../../shell/page-header';
import { FILTERS, MissionFilter, countBy, filterMissions } from './filter';

const REFRESH_MS = 10_000;

@Component({
  selector: 'app-missions',
  imports: [PageHeader, NgIcon, HlmButtonImports, HlmBadgeImports, HlmTableImports, HlmSheetImports, HlmSkeletonImports],
  providers: [provideIcons({ lucideChevronRight, lucideRefreshCw, lucideSearch })],
  templateUrl: './missions.html',
})
export class Missions {
  private readonly api = inject(AdminApi);

  /**
   * Le texte cherché depuis la barre du haut (/missions?q=…). Sans paramètre, le routeur passe undefined
   * (et non la valeur par défaut) : la transformation le ramène à une chaîne vide.
   */
  readonly q = input('', { transform: (value: string | undefined) => value ?? '' });

  protected readonly missions = signal<Mission[] | null>(null);
  protected readonly filter = signal<MissionFilter>('all');
  protected readonly query = signal('');
  protected readonly shown = computed(() => filterMissions(this.missions() ?? [], this.filter(), this.query()));
  protected readonly count = (f: MissionFilter) => countBy(this.missions() ?? [], f);

  // Détail
  protected readonly selected = signal<Mission | null>(null);
  protected readonly events = signal<AuditEntry[]>([]);
  protected readonly sheetState = computed<BrnDialogState>(() => (this.selected() ? 'open' : 'closed'));
  protected readonly readAll = signal(false);
  protected readonly deciding = signal(false);
  private readonly content = viewChild<ElementRef<HTMLElement>>('content');

  protected readonly FILTERS = FILTERS;
  protected readonly STATUS = STATUS;
  protected readonly describe = describe;
  protected readonly argumentsOf = argumentsOf;
  protected readonly when = when;

  constructor() {
    effect(() => this.query.set(this.q()));
    void this.load();
    // ponytail: rafraîchissement toutes les 10 s ; passer au hub SignalR (/hubs/missions) si le direct devient nécessaire.
    const timer = setInterval(() => void this.load(true), REFRESH_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected async load(quiet = false) {
    try {
      this.missions.set(await this.api.missions());
    } catch (e) {
      if (!quiet) toast.error(errorMessage(e));
    }
  }

  protected async open(mission: Mission) {
    this.readAll.set(false);
    this.selected.set(mission);
    try {
      const [fresh, events] = await Promise.all([this.api.mission(mission.id), this.api.events(mission.id)]);
      this.selected.set(fresh);
      this.events.set(events);
      setTimeout(() => this.checkRead(), 0);
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  /** « Approuver » ne s'active qu'une fois tout le contenu affiché : on n'approuve pas ce qu'on n'a pas vu. */
  protected checkRead() {
    const el = this.content()?.nativeElement;
    if (!el || el.scrollTop + el.clientHeight >= el.scrollHeight - 4) this.readAll.set(true);
  }

  protected async decide(approved: boolean) {
    const mission = this.selected();
    const approval = mission?.pendingApproval;
    if (!mission || !approval) return;
    this.deciding.set(true);
    try {
      // L'identifiant de la demande lie la décision au contenu affiché (leçon 25) : si la demande a changé, l'API refuse (409).
      await this.api.decide(mission.id, approval.id, approved);
      toast.success(approved ? 'Écriture approuvée : la mission reprend.' : 'Écriture refusée : l’agent en est informé.');
      this.selected.set(null);
      await this.load();
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      this.deciding.set(false);
    }
  }
}
