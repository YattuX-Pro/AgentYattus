import { Component, computed, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBot, lucideFileText, lucideFolderSearch, lucidePenLine, lucidePlus, lucideShieldCheck, lucideSparkles } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { AdminApi, Agent, AgentVersion, LlmModel, LlmStatus, errorMessage, selectionKey } from '../../core/api';
import { when } from '../../core/format';
import { diffLines } from './diff';
import { tokens } from '../../core/format';
import { PageHeader } from '../../shell/page-header';
import { ModelPicker } from '../models/model-picker';
import { AgentForm, TOOLS, formErrors } from './tools';

@Component({
  selector: 'app-agents',
  imports: [
    PageHeader, ModelPicker, NgIcon, HlmButtonImports, HlmBadgeImports, HlmSkeletonImports, HlmSheetImports,
    HlmTextareaImports, HlmInputImports,
  ],
  providers: [provideIcons({ lucideBot, lucideFileText, lucideFolderSearch, lucidePenLine, lucidePlus, lucideShieldCheck, lucideSparkles })],
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

  protected readonly toolChoices = TOOLS;

  // Nouvel agent (ADR 0013) et profil d'un agent créé
  protected readonly createOpen = signal(false);
  protected readonly createState = computed<BrnDialogState>(() => (this.createOpen() ? 'open' : 'closed'));
  protected readonly form = signal<AgentForm>({ name: '', summary: '', instructions: '', tools: ['list_files', 'read_file'] });
  protected readonly createErrors = computed(() => formErrors(this.form(), true));
  protected readonly profile = signal<AgentForm>({ name: '', summary: '', instructions: '', tools: [] });
  protected readonly profileErrors = computed(() => formErrors(this.profile(), false));
  protected readonly confirmRetire = signal(false);
  protected readonly providerName = (id: string) => this.status()?.providers.find((p) => p.id === id)?.name ?? id;
  protected readonly key = selectionKey;
  protected readonly when = when;

  // Édition d'un rôle (ADR 0011)
  protected readonly editingRole = signal<Agent | null>(null);
  protected readonly editState = computed<BrnDialogState>(() => (this.editingRole() ? 'open' : 'closed'));
  protected readonly draft = signal('');
  protected readonly maxTurns = signal('');
  protected readonly versions = signal<AgentVersion[]>([]);
  protected readonly diff = computed(() => {
    const role = this.editingRole();
    return role ? diffLines(role.defaultInstructions, this.draft()) : [];
  });
  protected readonly changedFromOrigin = computed(() => this.diff().some((l) => l.kind !== 'same'));
  protected readonly draftValid = computed(() => this.draft().trim().length >= 20 && this.draft().length <= 8000);

  protected async edit(agent: Agent) {
    this.editingRole.set(agent);
    this.confirmRetire.set(false);
    this.profile.set({ name: agent.name, summary: agent.role, instructions: '', tools: agent.tools.map((t) => t.name) });
    this.draft.set(agent.instructions);
    this.maxTurns.set(agent.maxIterationsOverride?.toString() ?? '');
    try {
      this.versions.set(await this.api.agentVersions(agent.key));
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  protected async saveRole(instructions: string | null, maxIterations: string, done: string) {
    const agent = this.editingRole();
    if (!agent) return;
    const turns = maxIterations.trim() ? Number(maxIterations) : null;
    this.busy.set(true);
    try {
      await this.api.updateAgent(agent.key, instructions, turns);
      toast.success(done);
      await this.load();
      const fresh = this.agents()?.find((a) => a.key === agent.key);
      if (fresh) await this.edit(fresh);
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected has(form: AgentForm, tool: string) {
    return form.tools.includes(tool);
  }

  protected toggle(which: 'form' | 'profile', tool: string) {
    const target = which === 'form' ? this.form : this.profile;
    target.update((f) => ({ ...f, tools: f.tools.includes(tool) ? f.tools.filter((t) => t !== tool) : [...f.tools, tool] }));
  }

  protected openCreate() {
    this.form.set({ name: '', summary: '', instructions: '', tools: ['list_files', 'read_file'] });
    this.createOpen.set(true);
  }

  protected async create() {
    const f = this.form();
    this.busy.set(true);
    try {
      const created = await this.api.createAgent({ name: f.name.trim(), summary: f.summary.trim(), instructions: f.instructions.trim(), tools: f.tools });
      this.createOpen.set(false);
      toast.success(`${created.name} rejoint les agents : on peut le choisir au lancement d’une mission et lui donner un visage.`);
      await this.load();
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async saveProfile() {
    const agent = this.editingRole();
    const p = this.profile();
    if (!agent) return;
    this.busy.set(true);
    try {
      await this.api.updateAgentProfile(agent.key, { name: p.name.trim(), summary: p.summary.trim(), tools: p.tools });
      toast.success('Profil enregistré.');
      await this.load();
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async retire() {
    const agent = this.editingRole();
    if (!agent) return;
    if (!this.confirmRetire()) {
      this.confirmRetire.set(true);
      return;
    }
    this.busy.set(true);
    try {
      await this.api.retireAgent(agent.key);
      this.editingRole.set(null);
      toast.success(`${agent.name} n’est plus proposé. Ses missions passées gardent son nom.`);
      await this.load();
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected restore(version: AgentVersion) {
    void this.saveRole(version.instructions, version.maxIterations?.toString() ?? '', `Version ${version.version} restaurée (nouvelle version créée).`);
  }
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
    if (agent.modelOverride) return 'choisi pour lui';
    return this.status()?.freeMode && this.status()?.freeChain?.length ? 'mode Gratuit' : 'par défaut';
  }

  protected icon(tool: string): string {
    return TOOLS.find((t) => t.name === tool)?.icon ?? 'lucideShieldCheck';
  }

  protected label(tool: string): string {
    return TOOLS.find((t) => t.name === tool)?.label ?? tool;
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
