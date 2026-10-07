// Shared behaviour for every page: theme switch, mobile sidebar, confirm dialogs, pagination links.

const rootElement = document.documentElement;

function saveTheme(themeName) {
    try {
        localStorage.setItem('acxiom-theme', themeName);
    } catch (storageError) {
        // Private mode or blocked storage: the theme still changes for this page.
    }
}

document.querySelectorAll('[data-theme-toggle]').forEach(toggleButton => {
    toggleButton.addEventListener('click', () => {
        const nextTheme = rootElement.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
        rootElement.setAttribute('data-theme', nextTheme);
        saveTheme(nextTheme);
        // Charts listen for this so they can redraw with the new colours.
        document.dispatchEvent(new CustomEvent('acxiom:themechange'));
    });
});

// ---------- Mobile sidebar ----------
const menuButton = document.querySelector('.menu-button');
const sidebar = document.getElementById('sidebar');
const sidebarScrim = document.querySelector('.sidebar-scrim');

function closeSidebar() {
    if (!sidebar) return;
    sidebar.classList.remove('open');
    menuButton?.setAttribute('aria-expanded', 'false');
    if (sidebarScrim) sidebarScrim.hidden = true;
}

menuButton?.addEventListener('click', () => {
    const isOpen = sidebar.classList.toggle('open');
    menuButton.setAttribute('aria-expanded', String(isOpen));
    sidebarScrim.hidden = !isOpen;
});
sidebarScrim?.addEventListener('click', closeSidebar);
document.addEventListener('keydown', keyEvent => {
    if (keyEvent.key === 'Escape' && sidebar?.classList.contains('open')) {
        closeSidebar();
        menuButton?.focus();
    }
});
window.matchMedia('(min-width: 901px)').addEventListener('change', closeSidebar);

// ---------- Confirm before delete / convert ----------
document.querySelectorAll('form[data-confirm]').forEach(confirmForm => {
    confirmForm.addEventListener('submit', submitEvent => {
        if (!window.confirm(confirmForm.dataset.confirm)) submitEvent.preventDefault();
    });
});

// ---------- Keep the current filters when paging or exporting ----------
document.querySelectorAll('[data-page]').forEach(pageLink => {
    const pageUrl = new URL(window.location.href);
    pageUrl.searchParams.set('page', pageLink.dataset.page);
    pageLink.href = pageUrl.pathname + pageUrl.search;
});
document.querySelectorAll('[data-export-link]').forEach(exportLink => {
    const exportUrl = new URL(window.location.href);
    exportUrl.searchParams.set('export', 'true');
    exportUrl.searchParams.delete('page');
    exportLink.href = exportUrl.pathname + exportUrl.search;
});
