import { Component, input } from '@angular/core';

/** L'en-tête commun des pages : titre, une phrase, et les actions à droite. */
@Component({
  selector: 'app-page-header',
  host: { class: 'mb-3 flex flex-wrap items-end justify-between gap-3' },
  template: `
    <div class="min-w-0">
      <h1 class="text-[22px] leading-tight font-semibold tracking-tight text-balance">{{ title() }}</h1>
      @if (description()) {
        <p class="text-muted-foreground mt-0.5">{{ description() }}</p>
      }
    </div>
    <div class="flex flex-wrap gap-2"><ng-content /></div>
  `,
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly description = input<string>();
}
