(() => {
  if (document.getElementById('exet-extension-badge')) return;

  const badge = document.createElement('div');
  badge.id = 'exet-extension-badge';
  badge.textContent = 'EXET Extension · Active';
  badge.title = 'This badge is injected by the bundled EXET sample extension.';
  document.documentElement.appendChild(badge);

  setTimeout(() => {
    badge.classList.add('exet-extension-hide');
    setTimeout(() => badge.remove(), 500);
  }, 9000);
})();
