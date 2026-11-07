import { Component, computed, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideImagePlus, lucideSearch, lucideTrash2 } from '@ng-icons/lucide';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { AdminApi, Portrait, errorMessage } from '../../core/api';
import { PageHeader } from '../../shell/page-header';
import { slugify } from '../organizations/slug';

/** Le filtre de la galerie : prénom ou identifiant, sans tenir compte des accents ni de la casse. */
export function matchesPortrait(p: Portrait, query: string): boolean {
  const fold = (s: string) => s.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();
  const q = fold(query.trim());
  return !q || fold(p.suggestedName).includes(q) || p.slug.includes(q);
}

@Component({
  selector: 'app-portraits',
  imports: [PageHeader, NgIcon, HlmButtonImports, HlmDialogImports, HlmFieldImports, HlmInputImports, HlmSkeletonImports, HlmSwitchImports],
  providers: [provideIcons({ lucideImagePlus, lucideSearch, lucideTrash2 })],
  templateUrl: './portraits.html',
})
export class Portraits {
  private readonly api = inject(AdminApi);

  protected readonly portraits = signal<Portrait[] | null>(null);
  protected readonly query = signal('');
  protected readonly shown = computed(() => (this.portraits() ?? []).filter((p) => matchesPortrait(p, this.query())));
  protected readonly activeCount = computed(() => (this.portraits() ?? []).filter((p) => p.active).length);

  protected readonly addState = signal<BrnDialogState>('closed');
  protected readonly newName = signal('');
  protected readonly newSlug = signal('');
  protected readonly slugEdited = signal(false);
  protected readonly work = signal<File | null>(null);
  protected readonly talk = signal<File | null>(null);
  protected readonly workPreview = signal<string | null>(null);
  protected readonly talkPreview = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly url = (path: string) => `/api${path}`;

  constructor() {
    void this.load();
  }

  protected async load() {
    try {
      this.portraits.set(await this.api.portraits(true));
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  protected rename(p: Portrait, name: string) {
    const trimmed = name.trim();
    if (trimmed && trimmed !== p.suggestedName) void this.update(p, trimmed, p.active);
  }

  protected async update(p: Portrait, name: string, active: boolean) {
    try {
      await this.api.updatePortrait(p.id, name, active);
      this.portraits.update((list) => list?.map((x) => (x.id === p.id ? { ...x, suggestedName: name, active } : x)) ?? null);
      toast.success(active === p.active ? `« ${name} » enregistré.` : active ? `${name} est de nouveau proposé.` : `${name} n’est plus proposé au recrutement.`);
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  protected async remove(p: Portrait) {
    try {
      await this.api.deletePortrait(p.id);
      this.portraits.update((list) => list?.filter((x) => x.id !== p.id) ?? null);
      toast.success(`${p.suggestedName} retiré de la galerie.`);
    } catch (e) {
      // 409 : utilisé par un collègue ou par les réglages ; le message du serveur dit de désactiver.
      toast.error(errorMessage(e));
    }
  }

  protected openAdd() {
    this.newName.set('');
    this.newSlug.set('');
    this.slugEdited.set(false);
    this.pick('travail', null);
    this.pick('parle', null);
    this.addState.set('open');
  }

  protected typeName(name: string) {
    this.newName.set(name);
    if (!this.slugEdited()) this.newSlug.set(slugify(name));
  }

  protected pick(pose: 'travail' | 'parle', file: File | null) {
    const [fileSignal, preview] = pose === 'travail' ? [this.work, this.workPreview] : [this.talk, this.talkPreview];
    const previous = preview();
    if (previous) URL.revokeObjectURL(previous);
    fileSignal.set(file);
    preview.set(file ? URL.createObjectURL(file) : null);
  }

  protected async add() {
    const work = this.work();
    if (!work) return;
    this.busy.set(true);
    try {
      const created = await this.api.addPortrait(this.newSlug().trim(), this.newName().trim(), work, this.talk());
      this.portraits.update((list) => [...(list ?? []), created].sort((a, b) => a.slug.localeCompare(b.slug)));
      this.addState.set('closed');
      toast.success(`${created.suggestedName} rejoint la galerie.`);
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
