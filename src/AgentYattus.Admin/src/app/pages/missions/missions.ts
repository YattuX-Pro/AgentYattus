import { Component, DestroyRef, ElementRef, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronRight, lucideRefreshCw, lucideSearch } from '@ng-icons/lucide';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { AdminApi, AuditEntry, Mission, errorMessage } from '../../core/api';
import { STATUS, argumentsOf, describe, timeline } from '../../core/events';
import { LiveService } from '../../core/live';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { debounceTime } from 'rxjs';
import { tokens, usd, when } from '../../core/format';
import { PageHeader } from '../../shell/page-header';
import { FILTERS, MissionFilter, countBy, filterMissions } from './filter';


@Component({
  selector: 'app-missions',
  imports: [PageHeader, RouterLink, NgIcon, HlmAlertDialogImports, HlmButtonImports, HlmBadgeImports, HlmTableImports, HlmSheetImports, HlmSkeletonImports],
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

  /** Les missions d'un seul projet (/missions?projet=…), depuis la page Projets. */
  readonly projet = input('', { transform: (value: string | undefined) => value ?? '' });

  protected readonly missions = signal<Mission[] | null>(null);
  protected readonly filter = signal<MissionFilter>('all');
  protected readonly query = signal('');
  protected readonly inProject = computed(() => (this.missions() ?? []).filter((m) => !this.projet() || m.projectId === this.projet()));
  protected readonly projectName = computed(() => this.inProject()[0]?.projectName ?? null);
  protected readonly shown = computed(() => filterMissions(this.inProject(), this.filter(), this.query()));
  protected readonly count = (f: MissionFilter) => countBy(this.inProject(), f);

  // Détail
  protected readonly selected = signal<Mission | null>(null);
  protected readonly events = signal<AuditEntry[]>([]);
  protected readonly sheetState = computed<BrnDialogState>(() => (this.selected() ? 'open' : 'closed'));
  protected readonly readAll = signal(false);
  protected readonly deciding = signal(false);
  protected readonly confirmStop = signal(false);
  protected readonly stopState = computed<BrnDialogState>(() => (this.confirmStop() ? 'open' : 'closed'));
  protected readonly canStop = (m: Mission) => m.status === 'Pending' || m.status === 'Running' || m.status === 'AwaitingApproval';
  protected readonly canRetry = (m: Mission) => (m.status === 'Failed' || m.status === 'Cancelled') && !!m.projectId;
  private readonly content = viewChild<ElementRef<HTMLElement>>('content');

  protected readonly FILTERS = FILTERS;
  protected readonly STATUS = STATUS;
  protected readonly describe = describe;
  protected readonly argumentsOf = argumentsOf;
  protected readonly when = when;
  protected readonly usd = usd;
  protected readonly tokens = tokens;

  constructor() {
    effect(() => this.query.set(this.q()));
    void this.load();
    // Le direct : chaque événement d'une mission recharge la liste (regroupés sur 300 ms) et le détail ouvert.
    const live = inject(LiveService);
    live.start();
    live.events.pipe(debounceTime(300), takeUntilDestroyed(inject(DestroyRef))).subscribe(() => {
      void this.load(true);
      const open = this.selected();
      if (open) void this.refreshDetail(open.id);
    });
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
      this.events.set(timeline(events));
      setTimeout(() => this.checkRead(), 0);
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  protected async stop() {
    const mission = this.selected();
    this.confirmStop.set(false);
    if (!mission) return;
    this.deciding.set(true);
    try {
      await this.api.cancel(mission.id);
      toast.success('Arrêt demandé : la mission s’arrête à sa prochaine étape.');
      await this.load();
      await this.open(mission);
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      this.deciding.set(false);
    }
  }

  protected async retry() {
    const mission = this.selected();
    if (!mission) return;
    this.deciding.set(true);
    try {
      const retry = await this.api.retry(mission.id);
      toast.success('Mission relancée.');
      await this.load();
      await this.open(retry);
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      this.deciding.set(false);
    }
  }

  /** Recharge le détail ouvert sans réinitialiser la lecture du contenu à approuver. */
  private async refreshDetail(id: string) {
    try {
      const [fresh, events] = await Promise.all([this.api.mission(id), this.api.events(id)]);
      if (this.selected()?.id !== id) return;
      if (fresh.pendingApproval?.id !== this.selected()?.pendingApproval?.id) this.readAll.set(false);
      this.selected.set(fresh);
      this.events.set(timeline(events));
      setTimeout(() => this.checkRead(), 0);
    } catch {
      // Le prochain événement réessaiera.
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
