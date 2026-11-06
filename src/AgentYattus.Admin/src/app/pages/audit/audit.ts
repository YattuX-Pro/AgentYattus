import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideDownload, lucideRefreshCw } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { AdminApi, AuditFeedEntry, errorMessage } from '../../core/api';
import { describe } from '../../core/events';
import { when } from '../../core/format';
import { PageHeader } from '../../shell/page-header';

const KINDS = [
  { id: 'all', label: 'Tous' },
  { id: 'statut', label: 'Statuts' },
  { id: 'modele', label: 'Modèles' },
  { id: 'outil', label: 'Outils' },
  { id: 'bascule', label: 'Bascules' },
  { id: 'decision', label: 'Décisions' },
];

@Component({
  selector: 'app-audit',
  imports: [PageHeader, RouterLink, NgIcon, HlmButtonImports, HlmBadgeImports, HlmTableImports, HlmSkeletonImports],
  providers: [provideIcons({ lucideDownload, lucideRefreshCw })],
  template: `
    <app-page-header title="Audit" description="Chaque action des agents et chaque décision, horodatées, en ajout seul.">
      <button hlmBtn variant="outline" (click)="load()"><ng-icon name="lucideRefreshCw" /> Actualiser</button>
      <button hlmBtn variant="outline" (click)="export()" [disabled]="exporting()"><ng-icon name="lucideDownload" /> Exporter en CSV</button>
    </app-page-header>

    <section class="bg-card rounded-xl border">
      <div class="flex flex-wrap items-center gap-1.5 border-b px-3.5 py-2.5">
        @for (k of KINDS; track k.id) {
          <button hlmBtn size="sm" [variant]="kind() === k.id ? 'secondary' : 'ghost'" [attr.aria-pressed]="kind() === k.id" (click)="kind.set(k.id)">
            {{ k.label }} <span class="num text-muted-foreground font-mono text-xs">{{ count(k.id) }}</span>
          </button>
        }
        <span class="text-muted-foreground ml-auto text-xs">{{ rows().length }} derniers événements</span>
      </div>
      <div hlmTableContainer>
        <table hlmTable class="font-mono text-xs">
          <thead hlmTHead>
            <tr hlmTr>
              <th hlmTh class="w-32">Heure</th>
              <th hlmTh class="w-24">Mission</th>
              <th hlmTh>Événement</th>
              <th hlmTh>Détail</th>
            </tr>
          </thead>
          <tbody hlmTBody>
            @if (!entries()) {
              @for (i of [1, 2, 3, 4, 5]; track i) {
                <tr hlmTr><td hlmTd colspan="4"><hlm-skeleton class="h-4 w-full" /></td></tr>
              }
            }
            @for (r of shown(); track $index) {
              <tr hlmTr>
                <td hlmTd class="text-muted-foreground num">{{ when(r.at) }}</td>
                <td hlmTd><a class="text-primary hover:underline" [routerLink]="['/missions']" [queryParams]="{ q: r.missionId }">{{ r.missionId.slice(0, 8) }}</a></td>
                <td hlmTd>
                  <span
                    [class.text-primary]="r.view.tone === 'success'"
                    [class.text-warning]="r.view.tone === 'warning'"
                    [class.text-destructive]="r.view.tone === 'destructive'"
                  >{{ r.view.label }}</span>
                </td>
                <td hlmTd class="text-muted-foreground max-w-[520px] truncate">{{ r.view.detail || '—' }}</td>
              </tr>
            } @empty {
              @if (entries()) {
                <tr hlmTr><td hlmTd colspan="4" class="text-muted-foreground py-8 text-center font-sans">Aucun événement de ce type.</td></tr>
              }
            }
          </tbody>
        </table>
      </div>
    </section>
  `,
})
export class Audit {
  private readonly api = inject(AdminApi);

  protected readonly entries = signal<AuditFeedEntry[] | null>(null);
  protected readonly kind = signal('all');
  protected readonly rows = computed(() => (this.entries() ?? []).map((e) => ({ ...e, view: describe(e.type) })));
  protected readonly shown = computed(() => this.rows().filter((r) => this.kind() === 'all' || r.view.kind === this.kind()));
  protected readonly count = (kind: string) => this.rows().filter((r) => kind === 'all' || r.view.kind === kind).length;
  protected readonly KINDS = KINDS;
  protected readonly when = when;
  protected readonly exporting = signal(false);

  protected async export() {
    this.exporting.set(true);
    try {
      const response = await this.api.exportAudit();
      const name = /filename="?([^";]+)"?/.exec(response.headers.get('Content-Disposition') ?? '')?.[1] ?? 'audit.csv';
      const url = URL.createObjectURL(response.body!);
      const link = Object.assign(document.createElement('a'), { href: url, download: name });
      link.click();
      URL.revokeObjectURL(url);
      toast.success('Export téléchargé (5 000 derniers événements).');
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      this.exporting.set(false);
    }
  }

  constructor() {
    void this.load();
  }

  protected async load() {
    try {
      this.entries.set(await this.api.audit());
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }
}
