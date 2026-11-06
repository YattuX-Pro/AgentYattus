import { Project } from '../../core/api';
import { filterProjects, scopes } from './logic';

const p = (name: string, org: string | null, owner = 'alice'): Project => ({
  id: name, name, organizationId: org, organizationName: org ? 'Studio' : null, owner: { id: owner, username: owner, displayName: null },
  visibility: 'Private', isPersonalDefault: false, workspace: null, myAccess: 'Reader', canManage: false, members: [], createdAt: '',
});
const all = [p('Site vitrine', null), p('API facturation', 'o1', 'marc'), p('App livreurs', 'o1')];

describe('filtres des projets', () => {
  it('filtre par espace et par texte (nom ou propriétaire)', () => {
    expect(filterProjects(all, 'personal', '').map((x) => x.name)).toEqual(['Site vitrine']);
    expect(filterProjects(all, 'o1', 'marc').map((x) => x.name)).toEqual(['API facturation']);
  });

  it('propose Tous, Personnels et chaque organisation avec son compte', () => {
    expect(scopes(all)).toEqual([
      { id: 'all', label: 'Tous', count: 3 },
      { id: 'personal', label: 'Personnels', count: 1 },
      { id: 'o1', label: 'Studio', count: 2 },
    ]);
  });
});
