import { Component, computed, inject, OnDestroy, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlay, lucideRotateCcw, lucideSave, lucideUserMinus } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { Agent, AdminApi, ColleagueEntry, Portrait, TeamSettings, errorMessage } from '../../core/api';
import { when } from '../../core/format';
import { PageHeader } from '../../shell/page-header';
import { PortraitPicker } from '../../shared/portrait-picker';

/** Une phrase d'exemple pour régler la vitesse : elle s'écrit à l'écran comme dans le bureau. */
export const SAMPLE = 'Je lis src/Orders/OrderService.cs.';

/** Combien de lettres sont écrites après ce temps, à cette vitesse. */
export function typedLength(elapsedMs: number, typingMs: number, length: number): number {
  return Math.min(length, Math.floor(elapsedMs / Math.max(1, typingMs)));
}

@Component({
  selector: 'app-teams',
  imports: [PageHeader, PortraitPicker, NgIcon, HlmBadgeImports, HlmButtonImports, HlmInputImports, HlmSkeletonImports, HlmTableImports],
  providers: [provideIcons({ lucidePlay, lucideRotateCcw, lucideSave, lucideUserMinus })],
  templateUrl: './teams.html',
})
export class Teams implements OnDestroy {
  private readonly api = inject(AdminApi);

  protected readonly portraits = signal<Portrait[]>([]);
  protected readonly roles = signal<Agent[]>([]);
  protected readonly form = signal<TeamSettings | null>(null);
  protected readonly colleagues = signal<ColleagueEntry[] | null>(null);
  protected readonly busy = signal(false);
  protected readonly preview = signal('');
  private timer: ReturnType<typeof setInterval> | undefined;

  protected readonly byId = computed(() => new Map(this.portraits().map((p) => [p.id, p])));
  protected readonly when = when;

  constructor() {
    void this.load();
  }

  ngOnDestroy() {
    clearInterval(this.timer);
  }

  protected async load() {
    try {
      const [portraits, settings, agents, colleagues] = await Promise.all([
        this.api.portraits(), this.api.teamSettings(), this.api.agents(), this.api.colleagues(),
      ]);
      this.portraits.set(portraits);
      this.form.set(settings);
      this.roles.set(agents);
      this.colleagues.set(colleagues);
      this.play();
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  protected seat(role: string) {
    return this.form()?.defaultTeam.find((s) => s.role === role) ?? null;
  }

  protected setSeat(role: string, patch: { portraitId?: string; name?: string }, suggested?: string) {
    this.form.update((f) => {
      if (!f) return f;
      const current = f.defaultTeam.find((s) => s.role === role);
      const next = { role, portraitId: patch.portraitId ?? current?.portraitId ?? '', name: patch.name ?? (current?.name || suggested || '') };
      // Choisir un visage propose son prénom, sauf si un prénom a déjà été tapé pour ce siège.
      if (patch.portraitId && suggested && !current?.name) next.name = suggested;
      return { ...f, defaultTeam: [...f.defaultTeam.filter((s) => s.role !== role), next].filter((s) => s.portraitId) };
    });
  }

  protected clearSeat(role: string) {
    this.form.update((f) => (f ? { ...f, defaultTeam: f.defaultTeam.filter((s) => s.role !== role) } : f));
  }

  protected set<K extends keyof TeamSettings>(key: K, value: TeamSettings[K]) {
    this.form.update((f) => (f ? { ...f, [key]: value } : f));
  }

  /** Rejoue la phrase d'exemple à la vitesse choisie. */
  protected play() {
    clearInterval(this.timer);
    const start = performance.now();
    this.timer = setInterval(() => {
      const f = this.form();
      if (!f) return;
      const n = typedLength(performance.now() - start, f.typingMs, SAMPLE.length);
      this.preview.set(SAMPLE.slice(0, n));
      if (n >= SAMPLE.length) clearInterval(this.timer);
    }, 16);
  }

  protected async save() {
    const form = this.form();
    if (!form) return;
    this.busy.set(true);
    try {
      await this.api.saveTeamSettings(form);
      toast.success('Équipe enregistrée : elle s’applique aux prochains projets et à la scène du bureau.');
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(entry: ColleagueEntry) {
    try {
      await this.api.removeColleague(entry.colleague.projectId, entry.colleague.id);
      this.colleagues.update((list) =>
        list?.map((e) => (e.colleague.id === entry.colleague.id ? { ...e, colleague: { ...e.colleague, active: false, removedAt: new Date().toISOString() } } : e)) ?? null);
      toast.success(`${entry.colleague.name} a quitté l’équipe de ${entry.projectName}.`);
    } catch (e) {
      // 403 : un administrateur de l'instance lit tout mais n'agit pas dans les projets privés des autres (ADR 0009).
      toast.error(errorMessage(e));
    }
  }
}
