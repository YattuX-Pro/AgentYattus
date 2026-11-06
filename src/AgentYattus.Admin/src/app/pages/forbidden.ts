import { Component, inject } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideShieldOff } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { AuthService } from '../core/auth';

@Component({
  selector: 'app-forbidden',
  imports: [HlmEmptyImports, HlmButtonImports, NgIcon],
  providers: [provideIcons({ lucideShieldOff })],
  template: `
    <div class="grid min-h-dvh place-items-center p-4">
      <div hlmEmpty>
        <div hlmEmptyHeader>
          <div hlmEmptyMedia variant="icon"><ng-icon name="lucideShieldOff" /></div>
          <div hlmEmptyTitle>Accès réservé aux administrateurs</div>
          <div hlmEmptyDescription>
            Le compte {{ auth.name() }} n'a pas le rôle administrateur. Le bureau et le mobile restent disponibles pour travailler.
          </div>
        </div>
        <div hlmEmptyContent>
          <button hlmBtn variant="outline" (click)="auth.logout()">Changer de compte</button>
        </div>
      </div>
    </div>
  `,
})
export class Forbidden {
  protected readonly auth = inject(AuthService);
}
