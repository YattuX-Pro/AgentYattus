import { AuditFeedEntry, Mission } from '../../core/api';

export interface Day { label: string; done: number; failed: number }

/** Tout ce que la vue d'ensemble affiche, calculé à partir des missions et du journal. */
export function stats(missions: Mission[], audit: AuditFeedEntry[], now = new Date(), days = 14) {
  const by = (s: Mission['status']) => missions.filter((m) => m.status === s).length;
  const completed = by('Completed');
  const failed = by('Failed');

  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate() - (days - 1));
  const perDay: Day[] = Array.from({ length: days }, (_, i) => {
    const d = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i);
    return { label: String(d.getDate()), done: 0, failed: 0 };
  });
  for (const m of missions) {
    const created = new Date(m.createdAt);
    const index = Math.floor((new Date(created.getFullYear(), created.getMonth(), created.getDate()).getTime() - start.getTime()) / 86_400_000);
    if (index < 0 || index >= days) continue;
    if (m.status === 'Completed') perDay[index].done++;
    if (m.status === 'Failed') perDay[index].failed++;
  }

  const usage = new Map<string, number>();
  for (const e of audit) if (e.type.startsWith('modele:')) usage.set(e.type.slice(7), (usage.get(e.type.slice(7)) ?? 0) + 1);
  const totalUses = [...usage.values()].reduce((a, b) => a + b, 0);
  const models = [...usage.entries()]
    .sort((a, b) => b[1] - a[1])
    .slice(0, 5)
    .map(([model, n]) => ({ model, share: Math.round((n / totalUses) * 100) }));

  return {
    total: missions.length,
    active: by('Pending') + by('Running'),
    awaiting: missions.filter((m) => m.status === 'AwaitingApproval'),
    running: missions.filter((m) => m.status === 'Running' || m.status === 'Pending'),
    completed,
    failed,
    successRate: completed + failed ? Math.round((completed / (completed + failed)) * 1000) / 10 : null,
    switches: audit.filter((e) => e.type.startsWith('bascule:')).length,
    toolCalls: audit.filter((e) => e.type.startsWith('outil:')).length,
    perDay,
    models,
  };
}

/** Un maximum « rond » pour l'axe du graphique (5, 10, 20, 50…), et ses graduations. */
export function axis(max: number): number[] {
  const steps = [5, 10, 20, 25, 50, 100, 200, 500, 1000];
  const top = steps.find((s) => s >= max) ?? Math.ceil(max / 1000) * 1000;
  return [0, 1, 2, 3, 4, 5].map((i) => (top / 5) * i);
}
