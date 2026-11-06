import { Project } from '../../core/api';

/** « all », « personal », ou l'identifiant d'une organisation. */
export type ProjectScope = string;

export function filterProjects(projects: Project[], scope: ProjectScope, query: string): Project[] {
  const q = query.trim().toLowerCase();
  return projects.filter(
    (p) =>
      (scope === 'all' || (scope === 'personal' ? p.organizationId === null : p.organizationId === scope)) &&
      (!q || `${p.name} ${p.owner.username} ${p.owner.displayName ?? ''}`.toLowerCase().includes(q)),
  );
}

/** Les espaces présents dans la liste, pour les filtres : Personnels, puis chaque organisation. */
export function scopes(projects: Project[]): { id: ProjectScope; label: string; count: number }[] {
  const orgs = new Map<string, { label: string; count: number }>();
  for (const p of projects)
    if (p.organizationId) {
      const o = orgs.get(p.organizationId) ?? { label: p.organizationName ?? 'Organisation', count: 0 };
      o.count++;
      orgs.set(p.organizationId, o);
    }
  return [
    { id: 'all', label: 'Tous', count: projects.length },
    { id: 'personal', label: 'Personnels', count: projects.filter((p) => !p.organizationId).length },
    ...[...orgs.entries()].map(([id, o]) => ({ id, ...o })),
  ];
}
