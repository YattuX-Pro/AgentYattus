import { TestBed } from '@angular/core/testing';
import { ThemeService } from './theme';

describe('ThemeService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('dark');
  });

  it('applique le thème choisi et le retient', () => {
    const theme = TestBed.inject(ThemeService);

    theme.choice.set('dark');
    TestBed.tick();
    expect(document.documentElement.classList.contains('dark')).toBe(true);
    expect(localStorage.getItem('theme')).toBe('dark');

    theme.choice.set('light');
    TestBed.tick();
    expect(document.documentElement.classList.contains('dark')).toBe(false);
  });
});
