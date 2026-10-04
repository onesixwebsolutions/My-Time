import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import {
  CompletedSpend,
  CompletedSpendRequest,
  ConstantExpense,
  ConstantExpenseRequest,
  ExpensesApi,
  VaryingExpense,
  VaryingExpenseRequest
} from '../../core/api/expenses.api';

const MONTH_NAMES = [
  'January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'
];

function pad2(n: number): string {
  return n < 10 ? `0${n}` : `${n}`;
}
function monthRange(year: number, month: number): { from: string; to: string } {
  const from = `${year}-${pad2(month + 1)}-01`;
  const lastDay = new Date(year, month + 1, 0).getDate();
  const to = `${year}-${pad2(month + 1)}-${pad2(lastDay)}`;
  return { from, to };
}
function formatMoney(n: number): string {
  return `₹${n.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

// Expenses module — monthly constant expenses (rent, subscriptions) and varying/one-off
// expenses, each with full CRUD. Varying expenses are scoped to a navigable month so their
// total is meaningfully comparable to (and combinable with) the constant-expenses total.
@Component({
  selector: 'app-expenses-page',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="mb-5 flex flex-wrap items-center gap-3">
      <div>
        <h1 class="text-[22px] font-bold tracking-tight text-text">Expenses</h1>
        <p class="mt-1 text-[13px] text-muted">Monthly constant expenses and day-to-day varying expenses.</p>
      </div>
      <div class="ml-auto flex items-center gap-2">
        <button type="button" (click)="prevMonth()" class="grid h-8 w-8 place-items-center rounded-lg border border-border text-muted hover:text-text">‹</button>
        <span class="w-[140px] text-center text-[13.5px] font-semibold text-text">{{ monthLabel() }}</span>
        <button type="button" (click)="nextMonth()" class="grid h-8 w-8 place-items-center rounded-lg border border-border text-muted hover:text-text">›</button>
      </div>
    </div>

    <div class="mb-5 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
      <div class="rounded-card border border-border bg-raised p-3.5">
        <span class="text-[10.5px] font-bold uppercase tracking-wider text-muted">Monthly constant</span>
        <b class="tnum mt-1 block text-[22px] font-bold tracking-tight">{{ formatMoney(constantTotal()) }}</b>
      </div>
      <div class="rounded-card border border-border bg-raised p-3.5">
        <span class="text-[10.5px] font-bold uppercase tracking-wider text-muted">Varying ({{ monthLabel() }})</span>
        <b class="tnum mt-1 block text-[22px] font-bold tracking-tight">{{ formatMoney(varyingTotal()) }}</b>
      </div>
      <div class="rounded-card border border-success bg-raised p-3.5">
        <span class="text-[10.5px] font-bold uppercase tracking-wider text-success">My spends completed this month</span>
        <b class="tnum mt-1 block text-[22px] font-bold tracking-tight text-success">{{ formatMoney(spendsTotal()) }}</b>
      </div>
      <div class="rounded-card border border-accent bg-raised p-3.5">
        <span class="text-[10.5px] font-bold uppercase tracking-wider text-accent">Combined total</span>
        <b class="tnum mt-1 block text-[22px] font-bold tracking-tight text-accent">{{ formatMoney(combinedTotal()) }}</b>
      </div>
    </div>

    <div class="grid items-start gap-5 lg:grid-cols-3">
      <!-- Monthly constant expenses -->
      <div class="overflow-hidden rounded-card border border-border bg-raised shadow-[var(--shadow)]">
        <div class="flex items-center gap-2.5 border-b border-border px-4 py-3.5">
          <h3 class="text-[11.5px] font-bold uppercase tracking-wider text-muted">Monthly Constant Expenses</h3>
          <span class="rounded-full border border-border bg-raised2 px-2.5 py-1 text-[11px] font-semibold text-muted">{{ constantExpenses().length }}</span>
          <label class="ml-auto flex items-center gap-1.5 text-[11px] text-muted">
            <input type="checkbox" [(ngModel)]="includeInactive" name="includeInactive" (change)="loadConstant()" />
            Show inactive
          </label>
          <button type="button" (click)="startAddConstant()" class="rounded-lg bg-accent px-3 py-1.5 text-[12px] font-semibold text-white shadow-[0_2px_8px_rgba(99,102,241,.3)]">
            + Add
          </button>
        </div>

        @if (addingConstant) {
          <div class="border-b border-border bg-raised2/40 p-4">
            <div class="grid grid-cols-[1fr_110px] gap-2.5">
              <input class="rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent focus:ring-2 focus:ring-accent-soft" placeholder="Name (e.g. Rent)" [(ngModel)]="draftName" name="draftName" />
              <input type="number" min="0" step="0.01" class="rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent" placeholder="Amount" [(ngModel)]="draftAmount" name="draftAmount" />
            </div>
            <div class="mt-2.5 grid grid-cols-[1fr_100px] gap-2.5">
              <input class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent" placeholder="Category (optional)" [(ngModel)]="draftCategory" name="draftCategory" />
              <input type="number" min="1" max="31" class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent" placeholder="Due day" [(ngModel)]="draftDayOfMonth" name="draftDayOfMonth" />
            </div>
            <textarea class="mt-2.5 w-full resize-none rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent" rows="2" placeholder="Notes (optional)" [(ngModel)]="draftNotes" name="draftNotes"></textarea>
            @if (error()) { <div class="mt-2 text-[12px] font-medium text-danger">{{ error() }}</div> }
            <div class="mt-3 flex justify-end gap-2">
              <button type="button" (click)="addingConstant = false" class="rounded-lg border border-border px-3 py-1.5 text-[12px] font-semibold text-muted">Cancel</button>
              <button type="button" (click)="createConstant()" class="rounded-lg bg-accent px-3.5 py-2 text-[12.5px] font-semibold text-white">Save</button>
            </div>
          </div>
        }

        @if (constantExpenses().length === 0 && !addingConstant) {
          <div class="px-4 py-10 text-center text-[13px] text-muted">No constant expenses yet — add your first one above.</div>
        }

        @for (e of constantExpenses(); track e.id) {
          @if (editingConstantId === e.id) {
            <div class="border-b border-border bg-raised2/40 p-4 last:border-b-0">
              <div class="grid grid-cols-[1fr_110px] gap-2.5">
                <input class="rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent" placeholder="Name" [(ngModel)]="editName" name="editName" />
                <input type="number" min="0" step="0.01" class="rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent" placeholder="Amount" [(ngModel)]="editAmount" name="editAmount" />
              </div>
              <div class="mt-2.5 grid grid-cols-[1fr_100px] gap-2.5">
                <input class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent" placeholder="Category (optional)" [(ngModel)]="editCategory" name="editCategory" />
                <input type="number" min="1" max="31" class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent" placeholder="Due day" [(ngModel)]="editDayOfMonth" name="editDayOfMonth" />
              </div>
              <textarea class="mt-2.5 w-full resize-none rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent" rows="2" placeholder="Notes (optional)" [(ngModel)]="editNotes" name="editNotes"></textarea>
              @if (error()) { <div class="mt-2 text-[12px] font-medium text-danger">{{ error() }}</div> }
              <div class="mt-3 flex justify-end gap-2">
                <button type="button" (click)="editingConstantId = null" class="rounded-lg border border-border px-3 py-1.5 text-[12px] font-semibold text-muted">Cancel</button>
                <button type="button" (click)="saveConstantEdit(e)" class="rounded-lg bg-accent px-3.5 py-2 text-[12.5px] font-semibold text-white">Save</button>
              </div>
            </div>
          } @else {
            <div class="flex items-center gap-3 border-b border-border px-4 py-3 last:border-b-0" [class.opacity-50]="!e.isActive">
              <div class="min-w-0 flex-1">
                <b class="block text-[13.2px] font-medium" [class.line-through]="!e.isActive">{{ e.name }}</b>
                <div class="mt-1 flex flex-wrap items-center gap-2 text-[10.5px] text-muted">
                  @if (e.category) { <span>{{ e.category }}</span> }
                  @if (e.dayOfMonth) { <span>· Due day {{ e.dayOfMonth }}</span> }
                  @if (!e.isActive) { <span class="text-warning">Inactive</span> }
                </div>
              </div>
              <b class="tnum text-[14px] font-bold">{{ formatMoney(e.amount) }}</b>
              <button type="button" (click)="toggleConstantActive(e)" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-muted hover:text-text">
                {{ e.isActive ? 'Pause' : 'Resume' }}
              </button>
              <button type="button" (click)="startEditConstant(e)" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-muted hover:text-text">Edit</button>
              <button type="button" (click)="removeConstant(e)" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-danger hover:bg-raised2">Delete</button>
            </div>
          }
        }
      </div>

      <!-- Varying expenses -->
      <div class="overflow-hidden rounded-card border border-border bg-raised shadow-[var(--shadow)]">
        <div class="flex items-center gap-2.5 border-b border-border px-4 py-3.5">
          <h3 class="text-[11.5px] font-bold uppercase tracking-wider text-muted">Varying Expenses</h3>
          <span class="rounded-full border border-border bg-raised2 px-2.5 py-1 text-[11px] font-semibold text-muted">{{ varyingExpenses().length }}</span>
          <button type="button" (click)="startAddVarying()" class="ml-auto rounded-lg bg-accent px-3 py-1.5 text-[12px] font-semibold text-white shadow-[0_2px_8px_rgba(99,102,241,.3)]">
            + Add
          </button>
        </div>

        @if (addingVarying) {
          <div class="border-b border-border bg-raised2/40 p-4">
            <div class="grid grid-cols-[1fr_110px] gap-2.5">
              <input class="rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent focus:ring-2 focus:ring-accent-soft" placeholder="What did you spend on?" [(ngModel)]="draftTitle" name="draftTitle" />
              <input type="number" min="0" step="0.01" class="rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent" placeholder="Amount" [(ngModel)]="draftAmount" name="draftVaryingAmount" />
            </div>
            <div class="mt-2.5 grid grid-cols-[1fr_150px] gap-2.5">
              <input class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent" placeholder="Category (optional)" [(ngModel)]="draftCategory" name="draftVaryingCategory" />
              <input type="date" class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent" [(ngModel)]="draftDate" name="draftDate" />
            </div>
            <textarea class="mt-2.5 w-full resize-none rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent" rows="2" placeholder="Notes (optional)" [(ngModel)]="draftNotes" name="draftVaryingNotes"></textarea>
            @if (error()) { <div class="mt-2 text-[12px] font-medium text-danger">{{ error() }}</div> }
            <div class="mt-3 flex justify-end gap-2">
              <button type="button" (click)="addingVarying = false" class="rounded-lg border border-border px-3 py-1.5 text-[12px] font-semibold text-muted">Cancel</button>
              <button type="button" (click)="createVarying()" class="rounded-lg bg-accent px-3.5 py-2 text-[12.5px] font-semibold text-white">Save</button>
            </div>
          </div>
        }

        @if (varyingExpenses().length === 0 && !addingVarying) {
          <div class="px-4 py-10 text-center text-[13px] text-muted">Nothing logged for {{ monthLabel() }} yet.</div>
        }

        @for (e of varyingExpenses(); track e.id) {
          @if (editingVaryingId === e.id) {
            <div class="border-b border-border bg-raised2/40 p-4 last:border-b-0">
              <div class="grid grid-cols-[1fr_110px] gap-2.5">
                <input class="rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent" placeholder="Title" [(ngModel)]="editTitle" name="editTitle" />
                <input type="number" min="0" step="0.01" class="rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent" placeholder="Amount" [(ngModel)]="editAmount" name="editVaryingAmount" />
              </div>
              <div class="mt-2.5 grid grid-cols-[1fr_150px] gap-2.5">
                <input class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent" placeholder="Category (optional)" [(ngModel)]="editCategory" name="editVaryingCategory" />
                <input type="date" class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent" [(ngModel)]="editDate" name="editDate" />
              </div>
              <textarea class="mt-2.5 w-full resize-none rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent" rows="2" placeholder="Notes (optional)" [(ngModel)]="editNotes" name="editVaryingNotes"></textarea>
              @if (error()) { <div class="mt-2 text-[12px] font-medium text-danger">{{ error() }}</div> }
              <div class="mt-3 flex justify-end gap-2">
                <button type="button" (click)="editingVaryingId = null" class="rounded-lg border border-border px-3 py-1.5 text-[12px] font-semibold text-muted">Cancel</button>
                <button type="button" (click)="saveVaryingEdit(e)" class="rounded-lg bg-accent px-3.5 py-2 text-[12.5px] font-semibold text-white">Save</button>
              </div>
            </div>
          } @else {
            <div class="flex items-center gap-3 border-b border-border px-4 py-3 last:border-b-0">
              <div class="min-w-0 flex-1">
                <b class="block text-[13.2px] font-medium">{{ e.title }}</b>
                <div class="mt-1 flex flex-wrap items-center gap-2 text-[10.5px] text-muted">
                  <span>{{ e.date }}</span>
                  @if (e.category) { <span>· {{ e.category }}</span> }
                </div>
              </div>
              <b class="tnum text-[14px] font-bold">{{ formatMoney(e.amount) }}</b>
              <button type="button" (click)="startEditVarying(e)" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-muted hover:text-text">Edit</button>
              <button type="button" (click)="removeVarying(e)" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-danger hover:bg-raised2">Delete</button>
            </div>
          }
        }
      </div>

      <!-- My Spends -->
      <div class="overflow-hidden rounded-card border border-border bg-raised shadow-[var(--shadow)]">
        <div class="flex items-center gap-2.5 border-b border-border px-4 py-3.5">
          <h3 class="text-[11.5px] font-bold uppercase tracking-wider text-muted">My Spends ({{ monthLabel() }})</h3>
          <span class="rounded-full border border-border bg-raised2 px-2.5 py-1 text-[11px] font-semibold text-muted">{{ completedSpends().length }}</span>
          <button type="button" (click)="startAddSpend()" class="ml-auto rounded-lg bg-accent px-3 py-1.5 text-[12px] font-semibold text-white shadow-[0_2px_8px_rgba(99,102,241,.3)]">
            + Add
          </button>
        </div>

        @if (addingSpend) {
          <div class="border-b border-border bg-raised2/40 p-4">
            <div class="grid grid-cols-[1fr_110px] gap-2.5">
              <input class="rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent focus:ring-2 focus:ring-accent-soft" placeholder="What did you spend on?" [(ngModel)]="draftSpendTitle" name="draftSpendTitle" />
              <input type="number" min="0" step="0.01" class="rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent" placeholder="Amount" [(ngModel)]="draftSpendAmount" name="draftSpendAmount" />
            </div>
            <div class="mt-2.5 grid grid-cols-[1fr_150px] gap-2.5">
              <input class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent" placeholder="Category (optional)" [(ngModel)]="draftSpendCategory" name="draftSpendCategory" />
              <input type="date" class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent" [(ngModel)]="draftSpendDate" name="draftSpendDate" />
            </div>
            <textarea class="mt-2.5 w-full resize-none rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent" rows="2" placeholder="Notes (optional)" [(ngModel)]="draftSpendNotes" name="draftSpendNotes"></textarea>
            @if (error()) { <div class="mt-2 text-[12px] font-medium text-danger">{{ error() }}</div> }
            <div class="mt-3 flex justify-end gap-2">
              <button type="button" (click)="addingSpend = false" class="rounded-lg border border-border px-3 py-1.5 text-[12px] font-semibold text-muted">Cancel</button>
              <button type="button" (click)="createSpend()" class="rounded-lg bg-accent px-3.5 py-2 text-[12.5px] font-semibold text-white">Save</button>
            </div>
          </div>
        }

        @if (completedSpends().length === 0 && !addingSpend) {
          <div class="px-4 py-10 text-center text-[13px] text-muted">Nothing spent yet for {{ monthLabel() }} — add it as you go.</div>
        }

        @for (e of completedSpends(); track e.id) {
          @if (editingSpendId === e.id) {
            <div class="border-b border-border bg-raised2/40 p-4 last:border-b-0">
              <div class="grid grid-cols-[1fr_110px] gap-2.5">
                <input class="rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent" placeholder="Title" [(ngModel)]="editSpendTitle" name="editSpendTitle" />
                <input type="number" min="0" step="0.01" class="rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent" placeholder="Amount" [(ngModel)]="editSpendAmount" name="editSpendAmount" />
              </div>
              <div class="mt-2.5 grid grid-cols-[1fr_150px] gap-2.5">
                <input class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent" placeholder="Category (optional)" [(ngModel)]="editSpendCategory" name="editSpendCategory" />
                <input type="date" class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent" [(ngModel)]="editSpendDate" name="editSpendDate" />
              </div>
              <textarea class="mt-2.5 w-full resize-none rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent" rows="2" placeholder="Notes (optional)" [(ngModel)]="editSpendNotes" name="editSpendNotes"></textarea>
              @if (error()) { <div class="mt-2 text-[12px] font-medium text-danger">{{ error() }}</div> }
              <div class="mt-3 flex justify-end gap-2">
                <button type="button" (click)="editingSpendId = null" class="rounded-lg border border-border px-3 py-1.5 text-[12px] font-semibold text-muted">Cancel</button>
                <button type="button" (click)="saveSpendEdit(e)" class="rounded-lg bg-accent px-3.5 py-2 text-[12.5px] font-semibold text-white">Save</button>
              </div>
            </div>
          } @else {
            <div class="flex items-center gap-3 border-b border-border px-4 py-3 last:border-b-0">
              <div class="min-w-0 flex-1">
                <b class="block text-[13.2px] font-medium">{{ e.title }}</b>
                <div class="mt-1 flex flex-wrap items-center gap-2 text-[10.5px] text-muted">
                  <span>{{ e.date }}</span>
                  @if (e.category) { <span>· {{ e.category }}</span> }
                </div>
              </div>
              <b class="tnum text-[14px] font-bold text-success">{{ formatMoney(e.amount) }}</b>
              <button type="button" (click)="startEditSpend(e)" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-muted hover:text-text">Edit</button>
              <button type="button" (click)="removeSpend(e)" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-danger hover:bg-raised2">Delete</button>
            </div>
          }
        }
      </div>
    </div>
  `
})
export class ExpensesPageComponent implements OnInit {
  private readonly api = inject(ExpensesApi);

  protected readonly constantExpenses = signal<ConstantExpense[]>([]);
  protected readonly varyingExpenses = signal<VaryingExpense[]>([]);
  protected readonly completedSpends = signal<CompletedSpend[]>([]);
  protected readonly error = signal<string | null>(null);
  protected includeInactive = false;

  protected readonly constantTotal = computed(() => this.constantExpenses().filter((e) => e.isActive).reduce((sum, e) => sum + e.amount, 0));
  protected readonly varyingTotal = computed(() => this.varyingExpenses().reduce((sum, e) => sum + e.amount, 0));
  protected readonly spendsTotal = computed(() => this.completedSpends().reduce((sum, e) => sum + e.amount, 0));
  protected readonly combinedTotal = computed(() => this.constantTotal() + this.varyingTotal());

  private viewYear: number;
  private viewMonth: number;

  protected addingConstant = false;
  protected addingVarying = false;
  protected addingSpend = false;
  protected editingConstantId: string | null = null;
  protected editingVaryingId: string | null = null;
  protected editingSpendId: string | null = null;

  // Shared field names across create/edit forms (constant + varying) — each form only reads the
  // subset it cares about, and every start*/save* method resets these before use, so there's no
  // cross-contamination between the four independent forms.
  protected draftName = '';
  protected draftTitle = '';
  protected draftAmount: number | null = null;
  protected draftCategory = '';
  protected draftDayOfMonth: number | null = null;
  protected draftDate = '';
  protected draftNotes = '';

  protected editName = '';
  protected editTitle = '';
  protected editAmount: number | null = null;
  protected editCategory = '';
  protected editDayOfMonth: number | null = null;
  protected editDate = '';
  protected editNotes = '';

  // "My Spends" gets its own dedicated fields (not shared with the constant/varying forms
  // above) so it can never cross-contaminate another form's state if more than one "+ Add"
  // panel is ever open at once.
  protected draftSpendTitle = '';
  protected draftSpendAmount: number | null = null;
  protected draftSpendCategory = '';
  protected draftSpendDate = '';
  protected draftSpendNotes = '';

  protected editSpendTitle = '';
  protected editSpendAmount: number | null = null;
  protected editSpendCategory = '';
  protected editSpendDate = '';
  protected editSpendNotes = '';

  constructor() {
    const now = new Date();
    this.viewYear = now.getFullYear();
    this.viewMonth = now.getMonth();
  }

  ngOnInit(): void {
    this.loadConstant();
    this.loadVarying();
    this.loadSpends();
  }

  protected monthLabel(): string {
    return `${MONTH_NAMES[this.viewMonth]} ${this.viewYear}`;
  }

  protected prevMonth(): void {
    this.viewMonth -= 1;
    if (this.viewMonth < 0) { this.viewMonth = 11; this.viewYear -= 1; }
    this.loadVarying();
    this.loadSpends();
  }

  protected nextMonth(): void {
    this.viewMonth += 1;
    if (this.viewMonth > 11) { this.viewMonth = 0; this.viewYear += 1; }
    this.loadVarying();
    this.loadSpends();
  }

  protected formatMoney(n: number): string {
    return formatMoney(n);
  }

  protected loadConstant(): void {
    this.api.listConstant(this.includeInactive).subscribe({
      next: (list) => this.constantExpenses.set(list),
      error: () => this.error.set('Could not load constant expenses.')
    });
  }

  private loadVarying(): void {
    const { from, to } = monthRange(this.viewYear, this.viewMonth);
    this.api.listVarying(from, to).subscribe({
      next: (list) => this.varyingExpenses.set(list),
      error: () => this.error.set('Could not load varying expenses.')
    });
  }

  private loadSpends(): void {
    const { from, to } = monthRange(this.viewYear, this.viewMonth);
    this.api.listSpends(from, to).subscribe({
      next: (list) => this.completedSpends.set(list),
      error: () => this.error.set('Could not load spends.')
    });
  }

  // --- Constant expense CRUD ---

  protected startAddConstant(): void {
    this.editingConstantId = null;
    this.draftName = '';
    this.draftAmount = null;
    this.draftCategory = '';
    this.draftDayOfMonth = null;
    this.draftNotes = '';
    this.addingConstant = !this.addingConstant;
    this.error.set(null);
  }

  protected createConstant(): void {
    const name = this.draftName.trim();
    if (!name || this.draftAmount === null) {
      this.error.set('Name and amount are required.');
      return;
    }
    const request: ConstantExpenseRequest = {
      name,
      amount: Number(this.draftAmount),
      category: this.draftCategory.trim() || null,
      dayOfMonth: this.draftDayOfMonth ? Number(this.draftDayOfMonth) : null,
      notes: this.draftNotes.trim() || null
    };
    this.api.createConstant(request).subscribe({
      next: (created) => {
        this.constantExpenses.update((list) => [...list, created].sort((a, b) => a.name.localeCompare(b.name)));
        this.addingConstant = false;
        this.error.set(null);
      },
      error: () => this.error.set('Could not create expense.')
    });
  }

  protected startEditConstant(e: ConstantExpense): void {
    this.addingConstant = false;
    this.editName = e.name;
    this.editAmount = e.amount;
    this.editCategory = e.category ?? '';
    this.editDayOfMonth = e.dayOfMonth;
    this.editNotes = e.notes ?? '';
    this.editingConstantId = e.id;
    this.error.set(null);
  }

  protected saveConstantEdit(e: ConstantExpense): void {
    const name = this.editName.trim();
    if (!name || this.editAmount === null) {
      this.error.set('Name and amount are required.');
      return;
    }
    const request: ConstantExpenseRequest = {
      name,
      amount: Number(this.editAmount),
      category: this.editCategory.trim() || null,
      dayOfMonth: this.editDayOfMonth ? Number(this.editDayOfMonth) : null,
      notes: this.editNotes.trim() || null
    };
    this.api.updateConstant(e.id, request).subscribe({
      next: (updated) => {
        this.constantExpenses.update((list) => list.map((x) => (x.id === e.id ? updated : x)));
        this.editingConstantId = null;
        this.error.set(null);
      },
      error: () => this.error.set('Could not save expense.')
    });
  }

  protected toggleConstantActive(e: ConstantExpense): void {
    this.api.setConstantActive(e.id, !e.isActive).subscribe({
      next: (updated) => {
        if (!this.includeInactive && !updated.isActive) {
          this.constantExpenses.update((list) => list.filter((x) => x.id !== e.id));
        } else {
          this.constantExpenses.update((list) => list.map((x) => (x.id === e.id ? updated : x)));
        }
      },
      error: () => this.error.set('Could not update expense.')
    });
  }

  protected removeConstant(e: ConstantExpense): void {
    this.api.removeConstant(e.id).subscribe({
      next: () => this.constantExpenses.update((list) => list.filter((x) => x.id !== e.id)),
      error: () => this.error.set('Could not delete expense.')
    });
  }

  // --- Varying expense CRUD ---

  protected startAddVarying(): void {
    this.editingVaryingId = null;
    const { from } = monthRange(this.viewYear, this.viewMonth);
    this.draftTitle = '';
    this.draftAmount = null;
    this.draftCategory = '';
    this.draftDate = from;
    this.draftNotes = '';
    this.addingVarying = !this.addingVarying;
    this.error.set(null);
  }

  protected createVarying(): void {
    const title = this.draftTitle.trim();
    if (!title || this.draftAmount === null || !this.draftDate) {
      this.error.set('Title, amount and date are required.');
      return;
    }
    const request: VaryingExpenseRequest = {
      title,
      amount: Number(this.draftAmount),
      category: this.draftCategory.trim() || null,
      date: this.draftDate,
      notes: this.draftNotes.trim() || null
    };
    this.api.createVarying(request).subscribe({
      next: (created) => {
        const { from, to } = monthRange(this.viewYear, this.viewMonth);
        if (created.date >= from && created.date <= to) {
          this.varyingExpenses.update((list) => [created, ...list]);
        }
        this.addingVarying = false;
        this.error.set(null);
      },
      error: () => this.error.set('Could not create expense.')
    });
  }

  protected startEditVarying(e: VaryingExpense): void {
    this.addingVarying = false;
    this.editTitle = e.title;
    this.editAmount = e.amount;
    this.editCategory = e.category ?? '';
    this.editDate = e.date.slice(0, 10);
    this.editNotes = e.notes ?? '';
    this.editingVaryingId = e.id;
    this.error.set(null);
  }

  protected saveVaryingEdit(e: VaryingExpense): void {
    const title = this.editTitle.trim();
    if (!title || this.editAmount === null || !this.editDate) {
      this.error.set('Title, amount and date are required.');
      return;
    }
    const request: VaryingExpenseRequest = {
      title,
      amount: Number(this.editAmount),
      category: this.editCategory.trim() || null,
      date: this.editDate,
      notes: this.editNotes.trim() || null
    };
    this.api.updateVarying(e.id, request).subscribe({
      next: () => {
        this.editingVaryingId = null;
        this.error.set(null);
        this.loadVarying();
      },
      error: () => this.error.set('Could not save expense.')
    });
  }

  protected removeVarying(e: VaryingExpense): void {
    this.api.removeVarying(e.id).subscribe({
      next: () => this.varyingExpenses.update((list) => list.filter((x) => x.id !== e.id)),
      error: () => this.error.set('Could not delete expense.')
    });
  }

  // --- My Spends CRUD ---

  protected startAddSpend(): void {
    this.editingSpendId = null;
    const { from } = monthRange(this.viewYear, this.viewMonth);
    this.draftSpendTitle = '';
    this.draftSpendAmount = null;
    this.draftSpendCategory = '';
    this.draftSpendDate = from;
    this.draftSpendNotes = '';
    this.addingSpend = !this.addingSpend;
    this.error.set(null);
  }

  protected createSpend(): void {
    const title = this.draftSpendTitle.trim();
    if (!title || this.draftSpendAmount === null || !this.draftSpendDate) {
      this.error.set('Title, amount and date are required.');
      return;
    }
    const request: CompletedSpendRequest = {
      title,
      amount: Number(this.draftSpendAmount),
      category: this.draftSpendCategory.trim() || null,
      date: this.draftSpendDate,
      notes: this.draftSpendNotes.trim() || null
    };
    this.api.createSpend(request).subscribe({
      next: (created) => {
        const { from, to } = monthRange(this.viewYear, this.viewMonth);
        if (created.date >= from && created.date <= to) {
          this.completedSpends.update((list) => [created, ...list]);
        }
        this.addingSpend = false;
        this.error.set(null);
      },
      error: () => this.error.set('Could not create spend.')
    });
  }

  protected startEditSpend(e: CompletedSpend): void {
    this.addingSpend = false;
    this.editSpendTitle = e.title;
    this.editSpendAmount = e.amount;
    this.editSpendCategory = e.category ?? '';
    this.editSpendDate = e.date.slice(0, 10);
    this.editSpendNotes = e.notes ?? '';
    this.editingSpendId = e.id;
    this.error.set(null);
  }

  protected saveSpendEdit(e: CompletedSpend): void {
    const title = this.editSpendTitle.trim();
    if (!title || this.editSpendAmount === null || !this.editSpendDate) {
      this.error.set('Title, amount and date are required.');
      return;
    }
    const request: CompletedSpendRequest = {
      title,
      amount: Number(this.editSpendAmount),
      category: this.editSpendCategory.trim() || null,
      date: this.editSpendDate,
      notes: this.editSpendNotes.trim() || null
    };
    this.api.updateSpend(e.id, request).subscribe({
      next: () => {
        this.editingSpendId = null;
        this.error.set(null);
        this.loadSpends();
      },
      error: () => this.error.set('Could not save spend.')
    });
  }

  protected removeSpend(e: CompletedSpend): void {
    this.api.removeSpend(e.id).subscribe({
      next: () => this.completedSpends.update((list) => list.filter((x) => x.id !== e.id)),
      error: () => this.error.set('Could not delete spend.')
    });
  }
}
