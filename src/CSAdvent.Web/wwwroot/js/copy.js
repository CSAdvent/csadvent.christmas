// Copy buttons: <button data-copy="element-id"> copies that element's text to the clipboard. The button says so for a moment,
// and so does a status line only screen readers hear. If the browser won't copy, the text is selected to copy by hand.
(() => {
    if (!document.querySelector('[data-copy]')) {
        return;
    }

    // A live region has to be on the page before its text changes, or screen readers don't announce it.
    const status = document.createElement('span');
    status.className = 'sr-only';
    status.setAttribute('role', 'status');
    document.body.append(status);

    // Each button's own label, so a second click while it says "Copied!" doesn't keep that for good.
    const labels = new WeakMap();
    const timers = new WeakMap();

    document.addEventListener('click', async (event) => {
        const button = event.target.closest('[data-copy]');
        const source = button && document.getElementById(button.dataset.copy);
        if (!source) {
            return;
        }

        if (!labels.has(button)) {
            labels.set(button, button.textContent);
        }
        clearTimeout(timers.get(button));

        let copied = true;
        try {
            await navigator.clipboard.writeText(source.textContent);
        } catch {
            // No clipboard here (not allowed, or not https): select the text instead.
            copied = false;
            getSelection().selectAllChildren(source);
        }

        button.textContent = copied ? 'Copied!' : "Couldn't copy";
        // Emptied first, so the same message is announced again on the next click.
        status.textContent = '';
        setTimeout(() => {
            status.textContent = copied ? 'Copied to the clipboard.' : "Couldn't copy. The text is selected, so copy it from there.";
        }, 100);
        timers.set(button, setTimeout(() => { button.textContent = labels.get(button); }, copied ? 1500 : 4000));
    });
})();
