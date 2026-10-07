// Loaded in <head> before the CSS paints, so the page never flashes the wrong theme.
// Saved choice wins; otherwise follow the operating system setting.
(function applySavedTheme() {
    var savedTheme = null;
    try {
        savedTheme = localStorage.getItem('acxiom-theme');
    } catch (storageError) {
        savedTheme = null;
    }
    var prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    var themeName = savedTheme === 'dark' || savedTheme === 'light' ? savedTheme : (prefersDark ? 'dark' : 'light');
    document.documentElement.setAttribute('data-theme', themeName);
})();
