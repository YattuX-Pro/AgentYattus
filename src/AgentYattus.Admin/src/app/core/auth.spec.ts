import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService, authInterceptor, realmRoles } from './auth';

const token = (payload: object) => `x.${btoa(JSON.stringify(payload)).replace(/=+$/, '')}.y`;

describe('realmRoles', () => {
  it('lit les rôles du realm, et rien d’un jeton illisible', () => {
    expect(realmRoles(token({ realm_access: { roles: ['llm-admin', 'x'] } }))).toEqual(['llm-admin', 'x']);
    expect(realmRoles(token({ realm_access: { roles: 'llm-admin' } }))).toEqual([]);
    expect(realmRoles('pas-un-jeton')).toEqual([]);
    expect(realmRoles(undefined)).toEqual([]);
  });
});

describe('authInterceptor', () => {
  it('n’envoie le jeton qu’à notre API', () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { accessToken: 'secret', login: () => Promise.resolve() } },
      ],
    });
    const http = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);

    http.get('/api/llm').subscribe();
    http.get('https://fonts.googleapis.com/css2').subscribe();

    expect(backend.expectOne('/api/llm').request.headers.get('Authorization')).toBe('Bearer secret');
    expect(backend.expectOne('https://fonts.googleapis.com/css2').request.headers.has('Authorization')).toBe(false);
  });
});
