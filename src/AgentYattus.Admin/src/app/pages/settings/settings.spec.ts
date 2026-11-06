import { parsePatterns } from './settings';

describe('parsePatterns', () => {
  it('une ligne par motif, sans vide ni doublon', () => {
    expect(parsePatterns('.env\n\n  *.pfx \n.env\n')).toEqual(['.env', '*.pfx']);
  });
});
