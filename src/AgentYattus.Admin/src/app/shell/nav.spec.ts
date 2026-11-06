import { locate } from './nav';

describe('locate', () => {
  it('retrouve le groupe et la page, paramètres compris', () => {
    expect(locate('/')).toEqual({ group: 'Pilotage', label: 'Vue d’ensemble' });
    expect(locate('/missions?q=revue')).toEqual({ group: 'Pilotage', label: 'Missions' });
    expect(locate('/modeles')).toEqual({ group: 'Configuration', label: 'Modèles' });
    expect(locate('/inconnu')).toBeNull();
  });
});
