import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { debounceTime } from 'rxjs';
import { LiveService } from '../../core/live';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRefreshCw } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { AdminApi, AuditFeedEntry, Budget, LlmStatus, Mission, errorMessage, selectionKey } from '../../core/api';
import { STATUS, describe } from '../../core/events';
import { tokens, usd, when } from '../../core/format';
import { PageHeader } from '../../shell/page-header';
import { axis, stats } from './stats';

const W = 720, H = 210, L = 30, B = 24, T = 12;

@Component({
  selector: 'app-overview',
  imports: [PageHeader, RouterLink, NgIcon, HlmButtonImports, HlmBadgeImports, HlmSkeletonImports],
  providers: [provideIcons({ lucideRefreshCw })],
  templateUrl: './overview.html',
})
export class Overview {
  private readonly api = inject(AdminApi);

  protected readonly missions = signal<Mission[] | null>(null);
  protected readonly audit = signal<AuditFeedEntry[]>([]);
  protected readonly llm = signal<LlmStatus | null>(null);
  protected readonly budget = signal<Budget | null>(null);
  protected readonly totalTokens = computed(() => (this.missions() ?? []).reduce((n, m) => n + (m.inputTokens ?? 0) + (m.outputTokens ?? 0), 0));
  protected readonly budgetPercent = computed(() => {
    const b = this.budget();
    return b?.monthlyBudget ? Math.min(100, Math.round((b.spent / b.monthlyBudget) * 100)) : 0;
  });
  protected readonly s = computed(() => stats(this.missions() ?? [], this.audit()));

  // Graphique : une seule échelle pour les barres, les graduations et les libellés.
  protected readonly W = W;
  protected readonly H = H;
  protected readonly L = L;
  protected readonly ticks = computed(() => axis(Math.max(1, ...this.s().perDay.map((d) => d.done + d.failed))));
  protected readonly y = (v: number) => H - B - (v / this.ticks().at(-1)!) * (H - B - T);
  protected readonly band = computed(() => (W - L - 8) / this.s().perDay.length);
  protected readonly bars = computed(() =>
    this.s().perDay.map((d, i) => {
      const x = L + i * this.band() + this.band() * 0.2;
      return { ...d, x, w: this.band() * 0.6, total: d.done + d.failed, last: i === this.s().perDay.length - 1 };
    }),
  );

  protected readonly defaultLabel = computed(() => {
    const status = this.llm();
    const d = status?.default;
    if (!d) return 'Aucun';
    const c = status!.connections.find((x) => x.id === d.connectionId);
    return `${c?.providerName ?? ''} · ${d.model}`;
  });
  protected readonly activeConnections = computed(() => this.llm()?.connections.filter((c) => !c.revokedAt).length ?? 0);
  protected readonly recent = computed(() => this.audit().slice(0, 8).map((e) => ({ ...e, view: describe(e.type) })));

  protected readonly STATUS = STATUS;
  protected readonly usd = usd;
  protected readonly tokens = tokens;
  protected readonly when = when;
  protected readonly key = selectionKey;

  constructor() {
    void this.load();
    const live = inject(LiveService);
    live.start();
    live.events.pipe(debounceTime(500), takeUntilDestroyed(inject(DestroyRef))).subscribe(() => void this.load());
  }

  protected async load() {
    try {
      const [missions, audit, llm, budget] = await Promise.all([this.api.missions(), this.api.audit(500), this.api.llm(), this.api.budget()]);
      this.missions.set(missions);
      this.audit.set(audit);
      this.llm.set(llm);
      this.budget.set(budget);
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }
}
