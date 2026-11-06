import { Component, computed, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowUp, lucidePlus, lucideRefreshCw, lucideSearch, lucideTriangleAlert, lucideX } from '@ng-icons/lucide';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmSwitchImports } from '@spartan-ng/helm/switch';
import { HlmTableImports } from '@spartan-ng/helm/table';
import {
  AdminApi, AuditFeedEntry, LlmConnection, LlmConnectionKind, LlmModel, LlmModels, LlmProvider, LlmSelection, LlmStatus, errorMessage, selectionKey,
} from '../../core/api';
import { context, price, when } from '../../core/format';
import { PageHeader } from '../../shell/page-header';
import { MAX_CHAIN, chainActivity, fillChain, filterModels, moveUp } from './logic';
import { ModelPicker } from './model-picker';

type PickerFor = 'default' | 'chain';

@Component({
  selector: 'app-models',
  imports: [
    PageHeader, ModelPicker, NgIcon, HlmButtonImports, HlmBadgeImports, HlmTableImports, HlmSwitchImports, HlmDialogImports,
    HlmAlertDialogImports, HlmFieldImports, HlmInputImports, HlmSkeletonImports,
  ],
  providers: [provideIcons({ lucideArrowUp, lucidePlus, lucideRefreshCw, lucideSearch, lucideTriangleAlert, lucideX })],
  templateUrl: './models.html',
})
export class Models {
  private readonly api = inject(AdminApi);

  protected readonly status = signal<LlmStatus | null>(null);
  protected readonly catalog = signal<LlmModels>({ models: [], errors: [] });
  protected readonly busy = signal(false);
  protected readonly audit = signal<AuditFeedEntry[]>([]);
  /** « fournisseur/modèle », le nom que le journal donne à un maillon de la chaîne. */
  protected readonly chainName = (s: LlmSelection) =>
    `${this.connections().find((c) => c.id === s.connectionId)?.provider ?? '?'}/${s.model}`;
  protected readonly activity = (s: LlmSelection) => chainActivity(this.chainName(s), this.audit());

  protected readonly providers = computed(() => this.status()?.providers ?? []);
  protected readonly connections = computed(() => this.status()?.connections ?? []);
  protected readonly chain = computed(() => this.status()?.freeChain ?? []);
  protected readonly providerName = (id: string) => this.providers().find((p) => p.id === id)?.name ?? id;
  protected readonly findModel = (s: LlmSelection | null | undefined) =>
    this.catalog().models.find((m) => selectionKey(m) === selectionKey(s));
  protected readonly defaultModel = computed(() => this.findModel(this.status()?.default));
  protected readonly connectionLabel = (id: string) => this.connections().find((c) => c.id === id)?.label ?? '';

  // Catalogue
  protected readonly query = signal('');
  protected readonly freeOnly = signal(false);
  protected readonly shown = computed(() => filterModels(this.catalog().models, this.query(), this.freeOnly(), this.providerName));

  // Sélecteur
  protected readonly pickerOpen = signal(false);
  protected readonly pickerFor = signal<PickerFor>('default');
  protected readonly pickerModels = computed(() =>
    this.pickerFor() === 'chain' ? this.catalog().models.filter((m) => m.free) : this.catalog().models,
  );

  // Connexion d'un fournisseur
  protected readonly connectState = signal<BrnDialogState>('closed');
  protected readonly providerQuery = signal('');
  protected readonly picked = signal<LlmProvider | null>(null);
  protected readonly label = signal('');
  protected readonly apiKey = signal('');
  protected readonly baseUrl = signal('');
  protected readonly connectError = signal('');
  protected readonly needsKey = computed(() => this.picked()?.kind === LlmConnectionKind.ApiKey);
  protected readonly hasAddress = computed(() => !!this.picked() && this.picked()!.kind !== LlmConnectionKind.ApiKey);
  protected readonly canConnect = computed(
    () => !!this.picked() && (!this.needsKey() || this.apiKey().length > 0) && (!this.hasAddress() || this.baseUrl().length > 0),
  );
  protected readonly filteredProviders = computed(() => {
    const q = this.providerQuery().trim().toLowerCase();
    return this.providers().filter((p) => !q || p.name.toLowerCase().includes(q) || p.id.includes(q));
  });

  // Révocation
  protected readonly pendingRevoke = signal<LlmConnection | null>(null);
  protected readonly revokeState = computed<BrnDialogState>(() => (this.pendingRevoke() ? 'open' : 'closed'));

  protected readonly Kind = LlmConnectionKind;
  protected readonly key = selectionKey;
  protected readonly context = context;
  protected readonly price = price;
  protected readonly when = when;
  protected readonly MAX_CHAIN = MAX_CHAIN;

  constructor() {
    void this.load();
  }

  protected async load() {
    try {
      const [status, catalog, audit] = await Promise.all([this.api.llm(), this.api.models(), this.api.audit(500)]);
      this.status.set(status);
      this.catalog.set(catalog);
      this.audit.set(audit);
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  private async run(action: () => Promise<unknown>, done?: string) {
    this.busy.set(true);
    try {
      await action();
      if (done) toast.success(done);
      await this.load();
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected openPicker(target: PickerFor) {
    this.pickerFor.set(target);
    this.pickerOpen.set(true);
  }

  protected choose(model: LlmModel) {
    const selection = { connectionId: model.connectionId, model: model.id };
    if (this.pickerFor() === 'chain') {
      if (this.chain().some((s) => selectionKey(s) === selectionKey(selection))) return;
      void this.saveFree(this.status()!.freeMode, [...this.chain(), selection]);
    } else {
      void this.run(() => this.api.setDefault(selection), `${model.name} est le modèle par défaut.`);
    }
  }

  protected setDefault(model: LlmModel) {
    this.pickerFor.set('default');
    this.choose(model);
  }

  protected saveFree(enabled: boolean, chain: LlmSelection[], done?: string) {
    return this.run(() => this.api.setFree(enabled, chain), done);
  }

  protected toggleFree(enabled: boolean) {
    void this.saveFree(enabled, this.chain(), enabled ? 'Mode Gratuit activé.' : 'Mode Gratuit désactivé.');
  }

  protected fill() {
    void this.saveFree(this.status()!.freeMode, fillChain(this.chain(), this.catalog().models, this.providers()));
  }

  protected up(index: number) {
    void this.saveFree(this.status()!.freeMode, moveUp(this.chain(), index));
  }

  protected remove(index: number) {
    void this.saveFree(this.status()!.freeMode, this.chain().filter((_, i) => i !== index));
  }

  protected openConnect() {
    this.picked.set(null);
    this.providerQuery.set('');
    this.connectError.set('');
    this.connectState.set('open');
  }

  protected pick(provider: LlmProvider) {
    this.picked.set(provider);
    this.label.set(provider.name);
    this.apiKey.set('');
    this.baseUrl.set(provider.defaultBaseUrl ?? '');
    this.connectError.set('');
  }

  protected async connect() {
    const provider = this.picked();
    if (!provider || !this.canConnect()) return;
    this.busy.set(true);
    try {
      await this.api.connect({
        provider: provider.id,
        label: this.label().trim() || provider.name,
        apiKey: this.needsKey() || this.apiKey() ? this.apiKey() : null,
        baseUrl: this.hasAddress() ? this.baseUrl().trim() : null,
      });
      this.connectState.set('closed');
      toast.success(`${provider.name} connecté. La clé est chiffrée sur le serveur et ne sera plus affichée.`);
      await this.load();
    } catch (e) {
      this.connectError.set(errorMessage(e));
    } finally {
      // La clé ne reste jamais dans la page, que la connexion ait réussi ou non.
      this.apiKey.set('');
      this.busy.set(false);
    }
  }

  protected confirmRevoke() {
    const connection = this.pendingRevoke();
    this.pendingRevoke.set(null);
    if (connection) void this.run(() => this.api.revoke(connection.id), `${connection.label} révoquée : la clé est effacée.`);
  }
}
