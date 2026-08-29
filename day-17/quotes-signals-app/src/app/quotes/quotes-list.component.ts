import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { QuotesPageState } from './quotes-page.state';

@Component({
  selector: 'app-quotes-list',
  imports: [RouterLink],
  providers: [QuotesPageState],
  templateUrl: './quotes-list.component.html',
  styleUrl: './quotes-list.component.scss',
})
export class QuotesListComponent {
  protected readonly state = inject(QuotesPageState);
}
