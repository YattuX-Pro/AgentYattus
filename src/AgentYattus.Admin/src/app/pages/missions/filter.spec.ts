import { Mission } from '../../core/api';
import { countBy, filterMissions } from './filter';

const m = (id: string, goal: string, status: Mission['status']): Mission =>
  ({ id, goal, status, createdAt: '', result: null, pendingApproval: null, kind: 'Question' });
const all = [m('0192aa', 'Revue du module Llm', 'AwaitingApproval'), m('0192bb', 'Question', 'Running'), m('0192cc', 'Autre', 'Pending')];

describe('filtre des missions', () => {
  it('filtre par état, texte ou début d’identifiant', () => {
    expect(filterMissions(all, 'active', '').map((x) => x.id)).toEqual(['0192bb', '0192cc']);
    expect(filterMissions(all, 'all', 'llm').map((x) => x.id)).toEqual(['0192aa']);
    expect(filterMissions(all, 'all', '0192bb').map((x) => x.id)).toEqual(['0192bb']);
    expect(countBy(all, 'AwaitingApproval')).toBe(1);
  });
});
