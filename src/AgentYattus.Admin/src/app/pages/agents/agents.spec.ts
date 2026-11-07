import { formErrors, successRate } from './tools';

describe('nouvel agent', () => {
  const ok = { name: 'Testeur', summary: 'Vérifie les tests.', instructions: 'Tu es le Testeur. Tu lis les tests et tu dis ce qui manque.', tools: ['read_file'] };

  it('accepte un agent complet', () => {
    expect(formErrors(ok, true)).toEqual([]);
  });

  it('signale un nom trop court, une présentation vide, aucun outil et une consigne trop courte', () => {
    expect(formErrors({ name: 'x', summary: '', instructions: 'court', tools: [] }, true)).toEqual(['name', 'summary', 'tools', 'instructions']);
  });

  it('ne demande pas la consigne quand on modifie seulement le profil', () => {
    expect(formErrors({ ...ok, instructions: '' }, false)).toEqual([]);
  });
});

describe('réussite d’un agent', () => {
  it('donne un pourcentage arrondi, et rien tant qu’aucune mission n’est terminée', () => {
    expect(successRate(3, 2)).toBe(67);
    expect(successRate(0, 0)).toBeNull();
  });
});
