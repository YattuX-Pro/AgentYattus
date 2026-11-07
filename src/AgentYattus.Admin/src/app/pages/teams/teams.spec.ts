import { SAMPLE, typedLength } from './teams';

describe('aperçu du rythme', () => {
  it('écrit une lettre par intervalle choisi, sans dépasser la phrase', () => {
    expect(typedLength(0, 38, SAMPLE.length)).toBe(0);
    expect(typedLength(380, 38, SAMPLE.length)).toBe(10);
    expect(typedLength(1e6, 38, SAMPLE.length)).toBe(SAMPLE.length);
    expect(typedLength(50, 0, 10)).toBe(10);
  });
});
