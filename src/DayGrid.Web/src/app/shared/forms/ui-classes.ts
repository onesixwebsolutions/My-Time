// Shared Tailwind class strings for the auth/account forms — same tokens as the existing pages
// (checklists, settings) so the new screens follow the active theme.
export const INPUT_CLASS =
  'w-full rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent focus:ring-2 focus:ring-accent-soft aria-[invalid=true]:border-danger';
export const LABEL_CLASS = 'mb-1 block text-[12.5px] font-semibold text-text';
export const PRIMARY_BUTTON_CLASS =
  'inline-flex items-center justify-center rounded-lg bg-accent px-3.5 py-2 text-[12.8px] font-semibold text-white shadow-[0_2px_8px_rgba(99,102,241,.3)] transition-transform hover:-translate-y-px disabled:cursor-not-allowed disabled:opacity-60 disabled:hover:translate-y-0';
export const SECONDARY_BUTTON_CLASS =
  'inline-flex items-center justify-center rounded-lg border border-border bg-raised px-3.5 py-2 text-[12.8px] font-semibold text-text transition-colors hover:bg-raised2 disabled:cursor-not-allowed disabled:opacity-60';
export const DANGER_BUTTON_CLASS =
  'inline-flex items-center justify-center rounded-lg bg-danger px-3.5 py-2 text-[12.8px] font-semibold text-white transition-opacity hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-60';
export const FIELD_ERROR_CLASS = 'mt-1 text-[12px] text-danger';
export const ERROR_BANNER_CLASS =
  'rounded-lg border border-danger bg-raised2 px-3 py-2.5 text-[12.8px] leading-relaxed text-danger';
export const INFO_BANNER_CLASS =
  'rounded-lg border border-accent bg-accent-soft px-3 py-2.5 text-[12.8px] leading-relaxed text-text';
export const LINK_CLASS = 'font-semibold text-accent hover:underline';
export const CARD_CLASS = 'rounded-card border border-border bg-raised p-5';

/** Bundle for templates: `protected readonly ui = UI;` then `[class]="ui.input"`. */
export const UI = {
  input: INPUT_CLASS,
  label: LABEL_CLASS,
  primary: PRIMARY_BUTTON_CLASS,
  secondary: SECONDARY_BUTTON_CLASS,
  danger: DANGER_BUTTON_CLASS,
  fieldError: FIELD_ERROR_CLASS,
  errorBanner: ERROR_BANNER_CLASS,
  infoBanner: INFO_BANNER_CLASS,
  link: LINK_CLASS,
  card: CARD_CLASS
} as const;
