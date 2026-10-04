// Apply the remembered theme toggle before first paint (key: AppShellComponent's THEME_STORAGE_KEY).
// Lives in a file (not inline in index.html) so the server's CSP can forbid inline scripts.
(function () {
  try {
    var t = localStorage.getItem('daygrid-theme');
    if (t === 'light' || t === 'dark') document.documentElement.setAttribute('data-theme', t);
  } catch (e) {}
})();
