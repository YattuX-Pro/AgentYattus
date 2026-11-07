import { Component, input, output } from '@angular/core';
import { Portrait } from '../core/api';

/** Une rangée de visages ; on en choisit un. Utilisée pour le chef d'équipe, « Vous » et l'équipe de départ. */
@Component({
  selector: 'app-portrait-picker',
  host: { class: 'block' },
  template: `
    <div role="radiogroup" [attr.aria-label]="label()" class="ay-scroll flex gap-2 overflow-x-auto pb-1">
      @for (p of portraits(); track p.id) {
        <button
          type="button"
          role="radio"
          [attr.aria-checked]="p.id === value()"
          [attr.aria-label]="p.suggestedName"
          [title]="p.suggestedName"
          (click)="changed.emit(p)"
          class="bg-muted relative size-14 shrink-0 overflow-hidden rounded-xl transition-[box-shadow,transform,opacity] duration-200 hover:-translate-y-0.5"
          [class.ring-primary]="p.id === value()"
          [class.ring-2]="p.id === value()"
          [class.ring-offset-2]="p.id === value()"
          [class.ring-offset-background]="p.id === value()"
          [class.opacity-60]="value() && p.id !== value()"
        >
          <img [src]="'/api' + p.workUrl" alt="" class="absolute top-[-6%] left-[-50%] h-[200%] w-[200%] max-w-none" />
        </button>
      }
    </div>
  `,
})
export class PortraitPicker {
  readonly portraits = input.required<Portrait[]>();
  readonly value = input<string | null>(null);
  readonly label = input('Portrait');
  readonly changed = output<Portrait>();
}
