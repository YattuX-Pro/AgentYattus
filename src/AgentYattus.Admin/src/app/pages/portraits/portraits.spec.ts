import { Portrait } from '../../core/api';
import { matchesPortrait } from './portraits';

const p = (slug: string, suggestedName: string): Portrait => ({ id: slug, slug, suggestedName, active: true, hasTalk: true, workUrl: '', talkUrl: '' });

describe('galerie de portraits', () => {
  it('filtre par prénom sans tenir compte des accents ni de la casse, ou par identifiant', () => {
    expect(matchesPortrait(p('aicha', 'Aïcha'), 'aic')).toBe(true);
    expect(matchesPortrait(p('ines', 'Inès'), 'INES')).toBe(true);
    expect(matchesPortrait(p('malik', 'Malik'), 'nora')).toBe(false);
    expect(matchesPortrait(p('malik', 'Malik'), '   ')).toBe(true);
  });
});
