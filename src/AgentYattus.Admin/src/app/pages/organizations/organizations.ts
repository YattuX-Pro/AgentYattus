import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBuilding2, lucidePlus, lucideRefreshCw, lucideX } from '@ng-icons/lucide';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmToggleGroupImports } from '@spartan-ng/helm/toggle-group';
import { AdminApi, ORG_ROLE, Organization, OrganizationRole, ProjectVisibility, UserRef, VISIBILITY, errorMessage } from '../../core/api';
import { UserPicker } from '../../shared/user-picker';
import { PageHeader } from '../../shell/page-header';
import { slugify } from './slug';

const OWNERSHIP = [
  ['Projets', 'Leur propriétaire', 'Privé, partagé à des personnes, ou ouvert à l’organisation'],
  ['Missions et rapports', 'Le projet', 'Visibles de ceux qui peuvent lire le projet'],
  ['Dossier de travail', 'Le projet', 'Choisi par un administrateur de l’instance ; les agents n’en sortent pas'],
  ['Membres et rôles', 'L’organisation', 'Gérés par ses administrateurs ; il en reste toujours un'],
  ['Comptes et mots de passe', 'Keycloak', 'Créés dans la console Keycloak ; l’annuaire se remplit à la connexion'],
  ['Modèles et clés', 'L’instance', 'Clés personnelles et budget par projet : prochaine étape'],
];

@Component({
  selector: 'app-organizations',
  imports: [
    PageHeader, UserPicker, NgIcon, HlmButtonImports, HlmBadgeImports, HlmTableImports, HlmToggleGroupImports,
    HlmDialogImports, HlmFieldImports, HlmInputImports, HlmEmptyImports,
  ],
  providers: [provideIcons({ lucideBuilding2, lucidePlus, lucideRefreshCw, lucideX })],
  templateUrl: './organizations.html',
})
export class Organizations {
  private readonly api = inject(AdminApi);

  protected readonly orgs = signal<Organization[] | null>(null);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly selected = computed<Organization | null>(
    () => this.orgs()?.find((o) => o.id === this.selectedId()) ?? this.orgs()?.at(0) ?? null,
  );
  protected readonly busy = signal(false);
  protected readonly name = signal('');
  protected readonly defaultVisibility = signal<ProjectVisibility>('Private');

  protected readonly createState = signal<BrnDialogState>('closed');
  protected readonly newName = signal('');
  protected readonly newSlug = signal('');
  protected readonly slugEdited = signal(false);
  protected readonly createError = signal('');

  protected readonly ORG_ROLE = ORG_ROLE;
  protected readonly roles = Object.keys(ORG_ROLE) as OrganizationRole[];
  protected readonly VISIBILITY = VISIBILITY;
  protected readonly visibilities = Object.keys(VISIBILITY) as ProjectVisibility[];
  protected readonly OWNERSHIP = OWNERSHIP;

  constructor() {
    void this.load();
  }

  protected async load() {
    try {
      this.orgs.set(await this.api.organizations());
      this.edit(this.selected());
    } catch (e) {
      toast.error(errorMessage(e));
    }
  }

  protected select(org: Organization) {
    this.selectedId.set(org.id);
    this.edit(org);
  }

  private edit(org: Organization | null) {
    this.name.set(org?.name ?? '');
    this.defaultVisibility.set(org?.defaultVisibility ?? 'Private');
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
      this.orgs.set(null);
      await this.load();
    } finally {
      this.busy.set(false);
    }
  }

  protected save(org: Organization) {
    void this.run(() => this.api.updateOrganization(org.id, this.name().trim(), this.defaultVisibility()), 'Organisation enregistrée.');
  }

  protected add(org: Organization, user: UserRef) {
    // Le moindre privilège : un nouveau membre arrive observateur ; on l'élève ensuite.
    void this.run(() => this.api.setOrgMember(org.id, user.id, 'Observer'), `${user.username} rejoint ${org.name} comme observateur.`);
  }

  protected setRole(org: Organization, userId: string, value: unknown) {
    const role = value as OrganizationRole;
    if (role && role in ORG_ROLE) void this.run(() => this.api.setOrgMember(org.id, userId, role), 'Rôle modifié.');
  }

  protected remove(org: Organization, userId: string) {
    void this.run(() => this.api.removeOrgMember(org.id, userId), 'Membre retiré.');
  }

  protected openCreate() {
    this.newName.set('');
    this.newSlug.set('');
    this.slugEdited.set(false);
    this.createError.set('');
    this.createState.set('open');
  }

  protected typeName(value: string) {
    this.newName.set(value);
    if (!this.slugEdited()) this.newSlug.set(slugify(value));
  }

  protected async create() {
    this.busy.set(true);
    try {
      const org = await this.api.createOrganization(this.newName().trim(), this.newSlug().trim());
      this.createState.set('closed');
      toast.success(`${org.name} créée. Vous en êtes administrateur.`);
      await this.load();
      this.select(org);
    } catch (e) {
      this.createError.set(e instanceof HttpErrorResponse && e.status === 409 ? 'Cet identifiant est déjà pris.' : errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
