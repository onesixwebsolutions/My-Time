/** Every top-level route, the sidebar link that reaches it, and the heading it renders. */
export const ROUTES: { path: string; nav: string | null; heading: RegExp }[] = [
  { path: '/today', nav: 'Today', heading: /\S/ },
  { path: '/checklists', nav: 'Checklists', heading: /^Checklists$/ },
  { path: '/timetable', nav: 'Timetable', heading: /^Timetable$/ },
  { path: '/timetable/schedule', nav: null, heading: /^Assignments$/ },
  { path: '/upcoming', nav: 'Upcoming', heading: /^Upcoming$/ },
  { path: '/tasks', nav: 'Tasks', heading: /^Tasks$/ },
  { path: '/expenses', nav: 'Expenses', heading: /^Expenses$/ },
  { path: '/calendar', nav: 'Calendar', heading: /^Calendar$/ },
  { path: '/insights', nav: 'Streaks', heading: /^Insights$/ },
  { path: '/settings', nav: 'Settings', heading: /^Settings$/ }
];
