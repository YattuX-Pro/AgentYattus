import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { AdminApi, Mission } from '../../core/api';
import { LiveService } from '../../core/live';
import { Missions } from './missions';

const mission: Mission = {
  id: '0192aaaa-0000-7000-8000-000000000000', goal: 'Revue du module Llm', status: 'Failed', kind: 'Review',
  createdAt: new Date().toISOString(), result: null, pendingApproval: null,
};

describe('page Missions', () => {
  it('s’affiche sans paramètre ?q= (le routeur passe alors undefined)', async () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: AdminApi, useValue: { missions: () => Promise.resolve([mission]) } },
        { provide: LiveService, useValue: { start: () => {}, events: new Subject() } },
      ],
    });
    const fixture = TestBed.createComponent(Missions);
    fixture.componentRef.setInput('q', undefined);
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent!;
    expect(text).toContain('Revue du module Llm');
    expect(text).toContain('Échouée');
    fixture.destroy();
  });
});
