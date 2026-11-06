import { tokens, usd } from './format';

describe('format', () => {
  it('affiche les montants sans les arrondir à zéro', () => {
    expect(usd(0)).toBe('0 $');
    expect(usd(4.8213)).toBe('4,82 $');
    expect(usd(0.0002)).toBe('0,0002 $');
  });

  it('abrège les tokens', () => {
    expect(tokens(950)).toBe('950');
    expect(tokens(12_345)).toBe('12,3k');
    expect(tokens(1_234_567)).toBe('1,2M');
  });
});
