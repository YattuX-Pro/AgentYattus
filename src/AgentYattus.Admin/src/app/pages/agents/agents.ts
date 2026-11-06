import { Component, computed, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBot, lucideRefreshCw, lucideShieldCheck } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { AdminApi, Agent, LlmModel, LlmStatus, errorMessage, selectionKey } from '../../core/api';
import { tokens } from '../../core/format';
import { PageHeader } from '../../shell/page-header';
import { ModelPicker } from '../models/model-picker';

/** Une ligne du tableau des outils : un outil, et les agents qui s'en servent. */
export function toolRows(agents: Agent[]) {
  const rows = new Map<string, { name: string; description: string; requiresApproval: boolean; agents: string[] }>();
  for (const agent of agents)
    for (const tool of agent.tools) {
      const row = rows.get(tool.name) ?? { ...tool, agents: [] };
      row.agents.push(agent.name);
      rows.set(tool.name, row);
    }
  return [...rows.values()];
}

@Component({
  selector: 'app-agents',
  imports: [PageHeader, ModelPicker, NgIcon, HlmButtonImports, HlmBadgeImports, HlmTableImports, HlmSkeletonImports],
  providers: [provideIcons({ lucideBot, lucideRefreshCw, lucideShieldCheck })],
  templateUrl: './agents.html',
})
export class Agents {
  private readonly api = inject(AdminApi);

  protected readonly agents = signal<Agent[] | null>(null);
  protected readonly models = signal<LlmModel[]>([]);
  protected readonly status = signal<LlmStatus | null>(null);
  protected readonly busy = signal(false);
  protected readonly editing = signal<Agent | null>(null);
  protected readonly pickerOpen = signal(false);

  protected readonly tools = computed(() => toolRows(this.agents() ?? []));
  protected readonly providerName = (id: string) => this.status()?.providers.find((p) => p.id === id)?.name ?? id;
  protected readonly key = selectionKey;
  protected readonly tokens = tokens;

  constructor() {
    void this.load();
  }

  protected async load() {
    try {
      const [agents, models, status] = await Promise.all([this.api.agents(), this.api.models(), this.api.llm()]);
      this.agents.set(agents);
      this.models.set(models.models);
      this.status.set(status);
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  /** Ce que l'agent utilise, dit simplement. */
  protected source(agent: Agent): string {
    if (agent.modelOverride) return 'Choisi pour ce rôle';
    return this.status()?.freeMode && this.status()?.freeChain?.length ? 'Suit le mode Gratuit' : 'Suit le modèle par défaut';
  }

  protected change(agent: Agent) {
    this.editing.set(agent);
    this.pickerOpen.set(true);
  }

  protected async set(agent: Agent, model: LlmModel | null) {
    this.busy.set(true);
    try {
      await this.api.setAgentModel(agent.key, model && { connectionId: model.connectionId, model: model.id });
      toast.success(model ? `${agent.name} utilise maintenant ${model.name}.` : `${agent.name} revient au modèle par défaut.`);
      await this.load();
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
