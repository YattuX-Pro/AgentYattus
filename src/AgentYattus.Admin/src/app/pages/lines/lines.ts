import { Component, computed, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideRotateCcw, lucideSave, lucideTrash2, lucideUndo2 } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { AdminApi, SceneLine, Trait, errorMessage } from '../../core/api';
import { PageHeader } from '../../shell/page-header';

/** Des valeurs d'exemple pour l'aperçu des bulles. */
export const SAMPLE: Record<string, string> = {
  objectif: 'Revue de Orders.Api', fichier: 'src/Orders/OrderService.cs', analyste: 'Inès', redacteur: 'Malik', chef: 'Léon', vous: 'Yatt',
};

const VARIABLE = /\{([^{}]*)\}/g;

/** Les variables d'une réplique que l'événement ne fournit pas (le serveur les refuse aussi). */
export function unknownVariables(template: string, allowed: string[]): string[] {
  return [...new Set([...template.matchAll(VARIABLE)].map((m) => m[1]).filter((v) => !allowed.includes(v)))];
}

/** La bulle telle qu'elle s'affichera, avec les valeurs d'exemple. */
export function fill(template: string, values: Record<string, string>): string {
  return template.replace(VARIABLE, (whole, name: string) => values[name] ?? whole);
}

const SPEAKERS: Partial<Record<string, string>> = { chef: 'Chef d’équipe', analyste: 'Analyste', redacteur: 'Rédacteur', vous: 'Vous' };

@Component({
  selector: 'app-lines',
  imports: [PageHeader, NgIcon, HlmBadgeImports, HlmButtonImports, HlmInputImports, HlmSkeletonImports],
  providers: [provideIcons({ lucidePlus, lucideRotateCcw, lucideSave, lucideTrash2, lucideUndo2 })],
  templateUrl: './lines.html',
})
export class Lines {
  private readonly api = inject(AdminApi);

  protected readonly traits = signal<Trait[] | null>(null);
  protected readonly lines = signal<SceneLine[] | null>(null);
  /** Les répliques modifiées dans l'écran : texte, ou null pour revenir à la réplique de départ. */
  protected readonly edits = signal<Record<string, string | null>>({});
  protected readonly busy = signal(false);
  protected readonly dirty = computed(() => Object.keys(this.edits()).length > 0);
  protected readonly speakers = SPEAKERS;
  protected readonly sample = SAMPLE;
  protected readonly fill = fill;
  protected readonly unknownVariables = unknownVariables;

  constructor() {
    void this.load();
  }

  protected async load() {
    try {
      const [traits, lines] = await Promise.all([this.api.traits(), this.api.lines()]);
      this.traits.set(traits);
      this.lines.set(lines);
      this.edits.set({});
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  protected text(line: SceneLine): string {
    const edit = this.edits()[line.event];
    return edit === undefined ? line.template : edit ?? '';
  }

  protected edit(line: SceneLine, value: string) {
    this.edits.update((e) => ({ ...e, [line.event]: value }));
  }

  protected insert(line: SceneLine, variable: string) {
    this.edit(line, `${this.text(line).trimEnd()} {${variable}}`.trim());
  }

  protected reset(line: SceneLine) {
    this.edits.update((e) => ({ ...e, [line.event]: null }));
  }

  protected setTrait(index: number, patch: Partial<Trait>) {
    this.traits.update((list) => list?.map((t, i) => (i === index ? { ...t, ...patch } : t)) ?? null);
  }

  protected addTrait() {
    this.traits.update((list) => [...(list ?? []), { key: '', label: '', style: '' }]);
  }

  protected removeTrait(index: number) {
    this.traits.update((list) => list?.filter((_, i) => i !== index) ?? null);
  }

  protected async saveTraits() {
    this.busy.set(true);
    try {
      await this.api.saveTraits(this.traits() ?? []);
      toast.success('Traits enregistrés : ils sont proposés dès le prochain recrutement.');
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async saveLines() {
    this.busy.set(true);
    try {
      await this.api.saveLines(this.edits());
      toast.success('Répliques enregistrées : elles s’affichent dès la prochaine scène.');
      await this.load();
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
