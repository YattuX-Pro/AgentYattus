import { diffLines } from './diff';

describe('diffLines', () => {
  it('garde les lignes communes et marque les retraits et les ajouts', () => {
    expect(diffLines('a\nb\nc', 'a\nx\nc')).toEqual([
      { kind: 'same', text: 'a' },
      { kind: 'removed', text: 'b' },
      { kind: 'added', text: 'x' },
      { kind: 'same', text: 'c' },
    ]);
    expect(diffLines('a', 'a').every((l) => l.kind === 'same')).toBe(true);
  });
});
