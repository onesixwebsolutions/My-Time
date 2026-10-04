import { newBrowserContext, newUserData, readState, submitLogin } from './support/auth';
import { apiPost, appDate, uid } from './support/data';
import { countEmails, waitForLink } from './support/email';
import { expect, test } from './support/fixtures';

// Two users in the real UI: A (the Admin storageState session, seeding through `api`) owns data;
// B registers through the UI and must see none of it — every list empty, A's detail URLs
// "not found" — while A's data stays intact.
test('two-user isolation: a new user sees none of the first user’s data', async ({ page, api, browser, guard }) => {
  const state = readState();
  const marker = uid('ISOLATED');
  const checklist = await apiPost<{ id: string }>(api, '/api/v1/checklists', {
    name: `${marker} checklist`,
    description: null,
    color: '#22c55e',
    icon: null,
    sortOrder: 0
  });
  const template = await apiPost<{ id: string }>(api, '/api/v1/timetable/templates', {
    name: `${marker} template`,
    description: null,
    dayStart: '06:00:00',
    dayEnd: '22:00:00',
    slotMinutes: 30
  });
  const task = await apiPost<{ id: string }>(api, '/api/v1/tasks', { title: `${marker} task`, notes: null, priority: 'Normal' });
  const future = await apiPost<{ id: string }>(api, '/api/v1/future-tasks', {
    title: `${marker} future`,
    notes: null,
    dueDate: appDate(2),
    dueTime: null,
    category: 'Bills',
    priority: 'High',
    reminders: []
  });
  const expense = await apiPost<{ id: string }>(api, '/api/v1/expenses/constant', {
    name: `${marker} rent`,
    amount: 1234,
    category: 'Housing',
    dayOfMonth: 1,
    notes: null
  });

  const bContext = await newBrowserContext(browser);
  const b = await bContext.newPage();
  guard.watch(b);
  guard.allowApiError(`/api/v1/checklists/${checklist.id}`, 404);
  guard.allowApiError(`/api/v1/timetable/templates/${template.id}`, 404);
  try {
    // B registers and confirms through the UI.
    const user = newUserData('isolated');
    const before = countEmails(state.pickupDir);
    await b.goto('/register');
    await b.locator('#register-name').fill(user.displayName);
    await b.locator('#register-email').fill(user.email);
    await b.locator('#register-password').fill(user.password);
    await b.getByRole('button', { name: 'Create account' }).click();
    await expect(b).toHaveURL(/\/register\/check-email/);
    await b.goto(await waitForLink(state.pickupDir, user.email, '/confirm-email', { since: before }));
    await expect(b.getByRole('heading', { level: 1 })).toHaveText('Email confirmed');
    await b.goto('/login');
    await submitLogin(b, user.email, user.password);
    await expect(b).toHaveURL(/\/today$/);

    const empty: [string, string][] = [
      ['/tasks', 'No tasks yet — add your first one above.'],
      ['/checklists', 'No checklists yet.'],
      ['/timetable', 'No templates yet.'],
      ['/timetable/schedule', 'No assignments yet.'],
      ['/upcoming', 'Nothing upcoming.'],
      ['/expenses', 'No constant expenses yet — add your first one above.'],
      ['/today', 'No checklists for today.']
    ];
    for (const [path, text] of empty) {
      await b.goto(path);
      await expect(b.getByText(text), path).toBeVisible();
      await expect(b.locator('body'), path).not.toContainText(marker);
    }

    // A's detail URLs don't exist for B (404 from the API -> "not found" in the UI).
    await b.goto(`/checklists/${checklist.id}`);
    await expect(b.getByText('Checklist not found.')).toBeVisible();
    await expect(b.locator('body')).not.toContainText(marker);
    await b.goto(`/timetable/${template.id}`);
    await expect(b.getByText('Template not found.')).toBeVisible();
    const direct = await b.evaluate(async (id) => (await fetch(`/api/v1/checklists/${id}`)).status, checklist.id);
    expect(direct).toBe(404);
  } finally {
    await bContext.close();
  }

  // A's data is untouched and still visible to A.
  for (const url of [`/api/v1/checklists/${checklist.id}`, `/api/v1/timetable/templates/${template.id}`]) {
    expect((await api.get(url)).status(), url).toBe(200);
  }
  await page.goto(`/checklists/${checklist.id}`);
  await expect(page.getByRole('heading', { level: 1 })).toContainText(`${marker} checklist`);
  await page.goto('/tasks');
  await expect(page.getByText(`${marker} task`)).toBeVisible();

  // Clean up so later specs' pages are not affected by A's extra rows.
  await api.delete(`/api/v1/checklists/${checklist.id}`);
  await api.delete(`/api/v1/timetable/templates/${template.id}`);
  await api.delete(`/api/v1/tasks/${task.id}`);
  await api.delete(`/api/v1/future-tasks/${future.id}`);
  await api.delete(`/api/v1/expenses/constant/${expense.id}`);
});
