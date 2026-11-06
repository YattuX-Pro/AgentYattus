import { slugify } from './slug';

describe('slugify', () => {
  it('propose un identifiant en minuscules, sans accents ni symboles', () => {
    expect(slugify('Studio Yattus & Cie')).toBe('studio-yattus-cie');
    expect(slugify('  Équipe Été  ')).toBe('equipe-ete');
    expect(slugify('x'.repeat(39) + ' y')).toBe('x'.repeat(39));
  });
});
