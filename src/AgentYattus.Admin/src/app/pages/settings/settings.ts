import { Component, computed, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRotateCcw, lucideSave } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { AdminApi, Budget, InstanceSettings, UpdateSettings, errorMessage } from '../../core/api';
import { usd } from '../../core/format';
import { PageHeader } from '../../shell/page-header';

/** Les motifs exclus, un par ligne dans l'écran, une liste pour l'API. */
export function parsePatterns(text: string): string[] {
  return [...new Set(text.split('\n').map((l) => l.trim()).filter(Boolean))];
}

@Component({
  selector: 'app-settings',
  imports: [PageHeader, NgIcon, HlmButtonImports, HlmInputImports, HlmSwitchImports, HlmTextareaImports, HlmSkeletonImports],
  providers: [provideIcons({ lucideRotateCcw, lucideSave })],
  templateUrl: './settings.html',
})
export class Settings {
  private readonly api = inject(AdminApi);

  protected readonly loaded = signal<InstanceSettings | null>(null);
  protected readonly form = signal<UpdateSettings | null>(null);
  protected readonly patterns = signal('');
  protected readonly budgetText = signal('');
  protected readonly budget = signal<Budget | null>(null);
  protected readonly busy = signal(false);
  protected readonly errors = signal<string>('');
  protected readonly spentPercent = computed(() => {
    const b = this.budget();
    return b?.monthlyBudget ? Math.min(100, Math.round((b.spent / b.monthlyBudget) * 100)) : 0;
  });
  protected readonly usd = usd;

  constructor() {
    void this.load();
  }

  protected async load() {
    try {
      const [settings, budget] = await Promise.all([this.api.settings(), this.api.budget()]);
      const { environment: _, ...editable } = settings;
      this.loaded.set(settings);
      this.form.set(editable);
      this.patterns.set(settings.excludedFiles.join('\n'));
      this.budgetText.set(settings.monthlyBudget?.toString().replace('.', ',') ?? '');
      this.budget.set(budget);
      this.errors.set('');
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  protected set<K extends keyof UpdateSettings>(key: K, value: UpdateSettings[K]) {
    this.form.update((f) => (f ? { ...f, [key]: value } : f));
  }

  protected number(key: keyof UpdateSettings, raw: string) {
    this.set(key, Number(raw) as never);
  }

  protected async save() {
    const form = this.form();
    if (!form) return;
    const budgetRaw = this.budgetText().trim().replace(',', '.');
    const monthlyBudget = budgetRaw ? Number(budgetRaw) : null;
    if (monthlyBudget !== null && Number.isNaN(monthlyBudget)) {
      this.errors.set('Le budget doit être un nombre, ou rester vide.');
      return;
    }
    this.busy.set(true);
    try {
      await this.api.saveSettings({ ...form, excludedFiles: parsePatterns(this.patterns()), monthlyBudget });
      toast.success('Réglages enregistrés : ils s’appliquent dès la prochaine mission.');
      await this.load();
    } catch (e) {
      this.errors.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
