import { Component, computed, input, model, output, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideSearch } from '@ng-icons/lucide';
import { BrnDialogState } from '@spartan-ng/brain/dialog';
import { HlmBadgeImports } from '@spartan-ng/helm/badge';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { LlmModel, selectionKey } from '../../core/api';
import { context, price } from '../../core/format';
import { filterModels } from './logic';

/** Le sélecteur de modèle : une recherche, une liste, un clic. Partagé par Modèles et Agents. */
@Component({
  selector: 'app-model-picker',
  imports: [HlmDialogImports, HlmBadgeImports, HlmEmptyImports, NgIcon],
  providers: [provideIcons({ lucideCheck, lucideSearch })],
  template: `
    <hlm-dialog [state]="state()" (closed)="open.set(false)">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="gap-0 p-0 sm:max-w-xl">
        <hlm-dialog-header class="border-b p-4">
          <h3 hlmDialogTitle>{{ title() }}</h3>
          <p hlmDialogDescription>{{ freeOnly() ? 'Seuls les modèles gratuits peuvent entrer dans la chaîne.' : 'Tous les modèles des fournisseurs connectés.' }}</p>
        </hlm-dialog-header>
        <label class="text-muted-foreground flex items-center gap-2 border-b px-4 py-2.5">
          <ng-icon name="lucideSearch" />
          <input
            class="text-foreground min-w-0 flex-1 bg-transparent outline-none"
            placeholder="Nom ou fournisseur"
            aria-label="Chercher un modèle"
            [value]="query()"
            (input)="query.set($any($event.target).value)"
          />
          <span class="num text-xs">{{ shown().length }}</span>
        </label>
        <div class="max-h-[55vh] overflow-y-auto p-1.5">
          @for (m of shown(); track key(m)) {
            <button
              type="button"
              class="hover:bg-accent flex w-full items-center gap-3 rounded-md px-3 py-2 text-left"
              (click)="chosen.emit(m); open.set(false)"
            >
              <div class="min-w-0 flex-1">
                <div class="truncate font-medium">{{ m.name }}</div>
                <div class="text-muted-foreground truncate font-mono text-xs">{{ providerName()(m.provider) }} · {{ m.id }}</div>
              </div>
              <span class="num text-muted-foreground text-xs">{{ context(m.contextWindow) }}</span>
              @if (m.free) {
                <span hlmBadge variant="success">gratuit</span>
              } @else {
                <span class="num text-muted-foreground w-28 text-right text-xs">{{ price(m.inputPricePerMillion, m.outputPricePerMillion) }}</span>
              }
              <ng-icon name="lucideCheck" class="text-primary" [class.invisible]="key(m) !== current()" />
            </button>
          } @empty {
            <div hlmEmpty class="p-6">
              <div hlmEmptyHeader>
                <div hlmEmptyTitle>Aucun modèle</div>
                <div hlmEmptyDescription>Connectez un fournisseur, ou changez la recherche.</div>
              </div>
            </div>
          }
        </div>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class ModelPicker {
  readonly open = model(false);
  readonly models = input.required<LlmModel[]>();
  readonly title = input('Choisir un modèle');
  readonly freeOnly = input(false);
  readonly current = input('');
  readonly providerName = input<(id: string) => string>((id) => id);
  readonly chosen = output<LlmModel>();

  protected readonly query = signal('');
  protected readonly state = computed<BrnDialogState>(() => (this.open() ? 'open' : 'closed'));
  protected readonly shown = computed(() => filterModels(this.models(), this.query(), this.freeOnly(), this.providerName()));
  protected readonly key = selectionKey;
  protected readonly context = context;
  protected readonly price = price;
}
