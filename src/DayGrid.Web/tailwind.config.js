/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ['./src/**/*.{html,ts}'],
  darkMode: ['selector', '[data-theme="dark"]'],
  theme: {
    extend: {
      colors: {
        // These map straight onto the CSS custom properties defined in
        // src/styles.scss (copied from the mockup's :root / [data-theme="light"]
        // blocks). Using var(...) directly (rather than the rgb()/<alpha-value>
        // trick) keeps every color usage — Tailwind class or hand-written CSS —
        // pointing at exactly the same source of truth, with zero risk of the
        // two systems drifting out of sync.
        surface: 'var(--surface)',
        raised: 'var(--raised)',
        raised2: 'var(--raised2)',
        border: 'var(--border)',
        text: 'var(--text)',
        muted: 'var(--muted)',
        accent: 'var(--accent)',
        'accent-soft': 'var(--accent-soft)',
        success: 'var(--success)',
        warning: 'var(--warning)',
        danger: 'var(--danger)',
        work: 'var(--work)',
        personal: 'var(--personal)',
        health: 'var(--health)',
        learning: 'var(--learning)',
        break: 'var(--break)',
        sleep: 'var(--sleep)',
        other: 'var(--other)'
      },
      borderRadius: {
        card: 'var(--r)'
      },
      fontFamily: {
        sans: ['Inter', '-apple-system', 'BlinkMacSystemFont', 'Segoe UI', 'Roboto', 'sans-serif']
      }
    }
  },
  plugins: []
};
