window.siqsTheme = {
    applyStored: () => {
        document.documentElement.dataset.theme = localStorage.getItem('siqs-theme') ?? 'dark';
    },
    set: theme => {
        localStorage.setItem('siqs-theme', theme);
        document.documentElement.dataset.theme = theme;
    },
    toggle: () => window.siqsTheme.set(
        document.documentElement.dataset.theme === 'light' ? 'dark' : 'light'),
};

window.siqsTheme.applyStored();
