import { Mission, MissionStatus } from '../../core/api';

export type MissionFilter = 'all' | 'active' | 'AwaitingApproval' | 'Completed' | 'Failed';

export const FILTERS: { id: MissionFilter; label: string }[] = [
  { id: 'all', label: 'Toutes' },
  { id: 'active', label: 'En cours' },
  { id: 'AwaitingApproval', label: 'À approuver' },
  { id: 'Completed', label: 'Terminées' },
  { id: 'Failed', label: 'Échouées' },
];

const matches = (status: MissionStatus, filter: MissionFilter) =>
  filter === 'all' || (filter === 'active' ? status === 'Pending' || status === 'Running' : status === filter);

/** Filtre par état et par texte (but ou identifiant). */
export function filterMissions(missions: Mission[], filter: MissionFilter, query: string): Mission[] {
  const q = query.trim().toLowerCase();
  return missions.filter((m) => matches(m.status, filter) && (!q || m.goal.toLowerCase().includes(q) || m.id.startsWith(q)));
}

export function countBy(missions: Mission[], filter: MissionFilter): number {
  return missions.filter((m) => matches(m.status, filter)).length;
}
