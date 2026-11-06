import { Agent } from '../../core/api';
import { toolRows } from './agents';

const agent = (name: string, tools: [string, boolean][]): Agent => ({
  name, role: '', model: '', maxIterations: 10, key: name.toLowerCase(), modelOverride: null,
  tools: tools.map(([n, a]) => ({ name: n, description: n, requiresApproval: a })),
});

describe('toolRows', () => {
  it('regroupe chaque outil avec les agents qui s’en servent', () => {
    const rows = toolRows([agent('Analyste', [['read_file', false]]), agent('Rédacteur', [['read_file', false], ['write_file', true]])]);
    expect(rows.map((r) => [r.name, r.agents.join('+'), r.requiresApproval])).toEqual([
      ['read_file', 'Analyste+Rédacteur', false],
      ['write_file', 'Rédacteur', true],
    ]);
  });
});
