import { Routes } from '@angular/router';

import { adminGuard, authGuard, guestGuard } from './core/auth/auth.guards';

// Route table. Every route is a lazy standalone component load; nothing here is eagerly
// bundled into main.js.
//
// Two layouts:
//  - public auth pages render inside AuthLayoutComponent (centered card, no sidebar);
//  - everything else renders inside AppShellComponent and requires a signed-in user.
// Both parents use path '' so the URLs stay flat (/login, /today, ...).
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'today' },
  {
    path: '',
    loadComponent: () => import('./features/auth/auth-layout.component').then((m) => m.AuthLayoutComponent),
    children: [
      {
        path: 'login',
        canActivate: [guestGuard],
        title: 'Sign in · My Time',
        loadComponent: () => import('./features/auth/login-page.component').then((m) => m.LoginPageComponent)
      },
      {
        path: 'register',
        canActivate: [guestGuard],
        title: 'Create account · My Time',
        loadComponent: () => import('./features/auth/register-page.component').then((m) => m.RegisterPageComponent)
      },
      {
        path: 'register/check-email',
        canActivate: [guestGuard],
        title: 'Check your email · My Time',
        loadComponent: () =>
          import('./features/auth/check-email-page.component').then((m) => m.CheckEmailPageComponent)
      },
      {
        // No guestGuard: the link from the email must work whatever state the browser is in.
        path: 'confirm-email',
        title: 'Confirm email · My Time',
        loadComponent: () =>
          import('./features/auth/confirm-email-page.component').then((m) => m.ConfirmEmailPageComponent)
      },
      {
        path: 'forgot-password',
        canActivate: [guestGuard],
        title: 'Forgot password · My Time',
        loadComponent: () =>
          import('./features/auth/forgot-password-page.component').then((m) => m.ForgotPasswordPageComponent)
      },
      {
        path: 'reset-password',
        title: 'Reset password · My Time',
        loadComponent: () =>
          import('./features/auth/reset-password-page.component').then((m) => m.ResetPasswordPageComponent)
      }
    ]
  },
  {
    path: '',
    canActivate: [authGuard],
    canActivateChild: [authGuard],
    title: 'My Time',
    loadComponent: () => import('./layout/app-shell.component').then((m) => m.AppShellComponent),
    children: [
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
      {
        path: 'account',
        loadComponent: () =>
          import('./features/account/account-page.component').then((m) => m.AccountPageComponent)
      },
      {
        path: 'admin/users',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/admin/admin-users-page.component').then((m) => m.AdminUsersPageComponent)
      }
    ]
  },
  { path: '**', redirectTo: 'today' }
];
