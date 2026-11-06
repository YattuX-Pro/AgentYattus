import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFolderOpen, lucidePlus, lucideRefreshCw, lucideSearch, lucideX } from '@ng-icons/lucide';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmToggleGroupImports } from '@spartan-ng/helm/toggle-group';
import {
  AdminApi, Me, Project, ProjectRole, ProjectVisibility, PROJECT_ROLE, UserRef, VISIBILITY, errorMessage,
} from '../../core/api';
import { when } from '../../core/format';
import { UserPicker } from '../../shared/user-picker';
import { PageHeader } from '../../shell/page-header';
import { ProjectScope, filterProjects, scopes } from './logic';

@Component({
  selector: 'app-projects',
  imports: [
    PageHeader, UserPicker, RouterLink, NgIcon, HlmButtonImports, HlmBadgeImports, HlmTableImports, HlmSkeletonImports,
    HlmToggleGroupImports, HlmDialogImports, HlmFieldImports, HlmInputImports,
  ],
  providers: [provideIcons({ lucideFolderOpen, lucidePlus, lucideRefreshCw, lucideSearch, lucideX })],
  templateUrl: './projects.html',
})
export class Projects {
  private readonly api = inject(AdminApi);

  protected readonly projects = signal<Project[] | null>(null);
  protected readonly me = signal<Me | null>(null);
  protected readonly scope = signal<ProjectScope>('all');
  protected readonly query = signal('');
  protected readonly selectedId = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly workspace = signal('');

  protected readonly scopes = computed(() => scopes(this.projects() ?? []));
  protected readonly shown = computed(() => filterProjects(this.projects() ?? [], this.scope(), this.query()));
  protected readonly selected = computed<Project | null>(() => this.projects()?.find((p) => p.id === this.selectedId()) ?? this.shown().at(0) ?? null);

  // Nouveau projet
  protected readonly createState = signal<BrnDialogState>('closed');
  protected readonly newName = signal('');
  protected readonly newOrg = signal<string>('personal');
  protected readonly newVisibility = signal<ProjectVisibility>('Private');
  /** Les organisations où l'on peut créer un projet : administrateur ou opérateur. */
  protected readonly creatableOrgs = computed(() => (this.me()?.organizations ?? []).filter((o) => o.role !== 'Observer'));

  protected readonly VISIBILITY = VISIBILITY;
  protected readonly PROJECT_ROLE = PROJECT_ROLE;
  protected readonly visibilities = Object.keys(VISIBILITY) as ProjectVisibility[];
  protected readonly when = when;

  constructor() {
    void this.load();
  }

  protected async load() {
    try {
      const [projects, me] = await Promise.all([this.api.projects(), this.api.me()]);
      this.projects.set(projects);
      this.me.set(me);
      this.workspace.set(this.selected()?.workspace ?? '');
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  protected select(project: Project) {
    this.selectedId.set(project.id);
    this.workspace.set(project.workspace ?? '');
  }

  private async run(action: () => Promise<unknown>, done: string) {
    this.busy.set(true);
    try {
      await action();
      toast.success(done);
      await this.load();
    } catch (e) {
      toast.error(errorMessage(e));
      // Refusé : une bascule a déjà changé d'état dans l'écran, mais la valeur du serveur n'a pas bougé,
      // donc Angular ne la repousserait pas. On redessine la section à partir du serveur.
      this.projects.set(null);
      await this.load();
    } finally {
      this.busy.set(false);
    }
  }

  protected setVisibility(project: Project, value: unknown) {
    const visibility = value as ProjectVisibility;
    if (!visibility || visibility === project.visibility) return;
    void this.run(() => this.api.updateProject(project.id, project.name, visibility), `${project.name} : ${VISIBILITY[visibility].label.toLowerCase()}.`);
  }

  protected invite(project: Project, user: UserRef) {
    void this.run(() => this.api.setProjectMember(project.id, user.id, 'Reader'), `${user.username} peut lire ${project.name}.`);
  }

  protected setRole(project: Project, userId: string, value: unknown) {
    const role = value as ProjectRole;
    if (role === 'Editor' || role === 'Reader')
      void this.run(() => this.api.setProjectMember(project.id, userId, role), 'Rôle modifié.');
  }

  protected remove(project: Project, userId: string) {
    void this.run(() => this.api.removeProjectMember(project.id, userId), 'Accès retiré.');
  }

  protected saveWorkspace(project: Project) {
    const path = this.workspace().trim() || null;
    void this.run(() => this.api.setWorkspace(project.id, path), path ? 'Dossier de travail enregistré.' : 'Retour au dossier par défaut.');
  }

  protected openCreate() {
    this.newName.set('');
    this.newOrg.set('personal');
    this.newVisibility.set('Private');
    this.createState.set('open');
  }

  protected async create() {
    const org = this.newOrg() === 'personal' ? null : this.newOrg();
    const visibility = !org && this.newVisibility() === 'Organization' ? 'Private' : this.newVisibility();
    this.busy.set(true);
    try {
      const project = await this.api.createProject(this.newName().trim(), org, visibility);
      this.createState.set('closed');
      toast.success(`${project.name} créé.`);
      await this.load();
      this.select(project);
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
