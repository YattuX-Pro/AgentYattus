import { Component, input } from '@angular/core';
import { PageHeader } from '../shell/page-header';

/** Page en attente : remplacée commit après commit. */
@Component({
  selector: 'app-soon',
  imports: [PageHeader],
  template: `<app-page-header [title]="title()" description="Cette page arrive dans un prochain commit." />`,
})
export class Soon {
  readonly title = input('');
}
