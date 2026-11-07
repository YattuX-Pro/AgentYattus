import { timeline, argumentsOf, describe as describeEvent, reveal } from './events';

describe('événements du journal', () => {
  it('traduit les événements connus', () => {
    expect(describeEvent('bascule:nvidia/llama')).toMatchObject({ label: 'Quota épuisé, bascule', detail: 'nvidia/llama', tone: 'warning' });
    expect(describeEvent('modele:openrouter/x:free').detail).toBe('openrouter/x:free');
    expect(describeEvent('AwaitingApproval')).toMatchObject({ label: 'À approuver', tone: 'warning' });
    expect(describeEvent('decision:refusee').tone).toBe('destructive');
  });

  it('montre les caractères invisibles d’un contenu à approuver', () => {
    expect(reveal('a​b‮c')).toBe('a⟦U+200B⟧b⟦U+202E⟧c');
    expect(reveal('ligne 1\nligne 2\t!')).toBe('ligne 1\nligne 2\t!');
    // Hors du plan de base (étiquettes), sélecteurs de variante, remplissage hangul, espace insécable.
    expect(reveal('x\u{E0041}y\uFE0Fz\u3164\u00A0')).toBe('x⟦U+E0041⟧y⟦U+FE0F⟧z⟦U+3164⟧⟦U+00A0⟧');
    expect(reveal('a\r\nb')).toBe('a␍\nb');
  });

  it('sépare les arguments et garde le texte brut', () => {
    expect(argumentsOf('{"path":"REPORT.md","content":"# Titre\\nTexte"}')).toEqual([
      { name: 'path', value: 'REPORT.md' },
      { name: 'content', value: '# Titre\nTexte' },
    ]);
    expect(argumentsOf('pas du json')).toEqual([{ name: 'arguments', value: 'pas du json' }]);
  });
});

describe('déroulé', () => {
  it('décrit qui a utilisé quel outil, et cache la ligne « outil » redondante', () => {
    expect(describeEvent('action:analyste:read_file:src/a:b.cs').detail).toBe('read_file · src/a:b.cs · analyste');
    const events = [{ type: 'outil:read_file' }, { type: 'action:analyste:read_file:a.cs' }, { type: 'Completed' }];
    expect(timeline(events).map((e) => e.type)).toEqual(['action:analyste:read_file:a.cs', 'Completed']);
    expect(timeline([{ type: 'outil:read_file' }]).map((e) => e.type)).toEqual(['outil:read_file']);
  });
});

