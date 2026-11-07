import { SAMPLE, fill, unknownVariables } from './lines';

describe('répliques', () => {
  it('remplit l’aperçu avec les valeurs d’exemple et garde visibles les variables inconnues', () => {
    expect(fill('Je lis {fichier}.', SAMPLE)).toBe('Je lis src/Orders/OrderService.cs.');
    expect(fill('Bonjour {inconnu}', SAMPLE)).toBe('Bonjour {inconnu}');
  });

  it('signale les variables que l’événement ne fournit pas', () => {
    expect(unknownVariables('Je lis {chemin} pour {vous}', ['fichier', 'vous'])).toEqual(['chemin']);
    expect(unknownVariables('Rien à remplacer', ['fichier'])).toEqual([]);
  });
});
