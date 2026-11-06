import { AuditFeedEntry, Mission } from '../../core/api';
import { axis, stats } from './stats';

const now = new Date(2026, 10, 6, 12);
const m = (status: Mission['status'], daysAgo: number): Mission => ({
  id: crypto.randomUUID(), goal: '', status, kind: 'Question', result: null, pendingApproval: null,
  createdAt: new Date(2026, 10, 6 - daysAgo, 9).toISOString(),
});
const e = (type: string): AuditFeedEntry => ({ missionId: 'x', type, at: '' });

describe('statistiques de la vue d’ensemble', () => {
  it('compte, range par jour et calcule les parts des modèles', () => {
    const s = stats(
      [m('Completed', 0), m('Completed', 0), m('Failed', 1), m('AwaitingApproval', 0), m('Running', 0), m('Completed', 30)],
      [e('modele:a'), e('modele:a'), e('modele:a'), e('modele:b'), e('bascule:b'), e('outil:read_file')],
      now,
    );
    expect([s.total, s.active, s.awaiting.length, s.completed, s.failed]).toEqual([6, 1, 1, 3, 1]);
    expect(s.successRate).toBe(75);
    expect(s.perDay.at(-1)).toEqual({ label: '6', done: 2, failed: 0 });
    expect(s.perDay.at(-2)).toEqual({ label: '5', done: 0, failed: 1 });
    expect(s.models).toEqual([{ model: 'a', share: 75 }, { model: 'b', share: 25 }]);
    expect([s.switches, s.toolCalls]).toEqual([1, 1]);
  });

  it('choisit un axe rond', () => {
    expect(axis(3)).toEqual([0, 1, 2, 3, 4, 5]);
    expect(axis(17)).toEqual([0, 4, 8, 12, 16, 20]);
    expect(stats([], [], now).successRate).toBeNull();
  });
});
