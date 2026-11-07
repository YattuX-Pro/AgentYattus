/** Le menu latéral : trois groupes, chacun s'ouvre et se ferme. */
export interface NavItem {
  path: string;
  label: string;
  icon: string;
}
export interface NavGroup {
  label: string;
  items: NavItem[];
}

export const NAV: NavGroup[] = [
  {
    label: 'Pilotage',
    items: [
      { path: '/', label: 'Vue d’ensemble', icon: 'lucideLayoutDashboard' },
      { path: '/projets', label: 'Projets', icon: 'lucideFolderKanban' },
      { path: '/missions', label: 'Missions', icon: 'lucideActivity' },
      { path: '/audit', label: 'Audit', icon: 'lucideScrollText' },
    ],
  },
  {
    label: 'Configuration',
    items: [
      { path: '/agents', label: 'Agents', icon: 'lucideBot' },
      { path: '/modeles', label: 'Modèles', icon: 'lucideCpu' },
      { path: '/portraits', label: 'Portraits', icon: 'lucideImages' },
    ],
  },
  {
    label: 'Administration',
    items: [
      { path: '/organisations', label: 'Organisations', icon: 'lucideBuilding2' },
      { path: '/utilisateurs', label: 'Utilisateurs', icon: 'lucideUsers' },
      { path: '/reglages', label: 'Réglages', icon: 'lucideSettings' },
    ],
  },
];

/** Le groupe et la page qui correspondent à une adresse, pour le fil d'Ariane. */
export function locate(url: string): { group: string; label: string } | null {
  const path = url.split(/[?#]/)[0];
  for (const group of NAV)
    for (const item of group.items)
      if (item.path === '/' ? path === '/' : path === item.path || path.startsWith(item.path + '/'))
        return { group: group.label, label: item.label };
  return null;
}
