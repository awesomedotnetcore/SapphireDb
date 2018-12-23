import { NgModule } from '@angular/core';
import { Routes, RouterModule } from '@angular/router';
import {MainComponent} from './main/main.component';
import {TestComponent} from './test/test.component';
import {AuthComponent} from './auth/auth.component';
import {CollectionTestComponent} from './collection-test/collection-test.component';
import {AuthGuard} from '../shared/auth.guard';

const routes: Routes = [
  { path: '', component: MainComponent, canActivate: [AuthGuard] },
  { path: 'test', component: TestComponent },
  { path: 'auth', component: AuthComponent },
  { path: 'collection', component: CollectionTestComponent }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class DashboardRoutingModule { }
