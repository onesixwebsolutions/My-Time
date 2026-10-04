import { Routes } from '@angular/router';

// Route table — mirrors plan section 6.1 exactly. Every route is a lazy
// standalone component load; nothing here is eagerly bundled into main.js.
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'today' },
  {
    path: 'today',
    loadComponent: () => import('./features/today/home-page.component').then((m) => m.HomePageComponent)
  },
  {
    path: 'today/:date',
    loadComponent: () => import('./features/today/home-page.component').then((m) => m.HomePageComponent)
  },
  {
    path: 'checklists',
    loadComponent: () =>
      import('./features/checklists/checklists-page.component').then((m) => m.ChecklistsPageComponent)
  },
  {
    path: 'checklists/:id',
    loadComponent: () =>
      import('./features/checklists/checklist-detail-page.component').then(
        (m) => m.ChecklistDetailPageComponent
      )
  },
  {
    path: 'timetable',
    loadComponent: () =>
      import('./features/timetable/timetable-page.component').then((m) => m.TimetablePageComponent)
  },
  {
    path: 'timetable/schedule',
    loadComponent: () =>
      import('./features/timetable/assignments-page.component').then((m) => m.AssignmentsPageComponent)
  },
  {
    path: 'timetable/:id',
    loadComponent: () =>
      import('./features/timetable/timetable-detail-page.component').then((m) => m.TimetableDetailPageComponent)
  },
  {
    path: 'upcoming',
    loadComponent: () =>
      import('./features/upcoming/upcoming-page.component').then((m) => m.UpcomingPageComponent)
  },
  {
    path: 'tasks',
    loadComponent: () => import('./features/tasks/tasks-page.component').then((m) => m.TasksPageComponent)
  },
  {
    path: 'expenses',
    loadComponent: () =>
      import('./features/expenses/expenses-page.component').then((m) => m.ExpensesPageComponent)
  },
  {
    path: 'calendar',
    loadComponent: () =>
      import('./features/calendar/calendar-page.component').then((m) => m.CalendarPageComponent)
  },
  {
    path: 'insights',
    loadComponent: () =>
      import('./features/insights/insights-page.component').then((m) => m.InsightsPageComponent)
  },
  {
    path: 'settings',
    loadComponent: () =>
      import('./features/settings/settings-page.component').then((m) => m.SettingsPageComponent)
  },
  { path: '**', redirectTo: 'today' }
];
