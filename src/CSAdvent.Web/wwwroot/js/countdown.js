// Counts down to the next reveal on the calendar (#31): <time data-countdown="{reveal}" data-now="{server time}">.
// The server's time corrects the visitor's clock, so a fast clock can't reload before the reveal. At zero the page reloads
// once, a few seconds late (and spread out, so every open calendar doesn't arrive at the same moment), to show the posts.
// The site loads no Blazor script, so following a link always loads a whole page, and this runs again on each one (#156).
(() => {
    const countdowns = document.querySelectorAll('time[data-countdown]');
    if (countdowns.length === 0) {
        return;
    }

    const pad = (value) => String(value).padStart(2, '0');
    let reloading = false;

    const timers = [...countdowns].map((element) => ({
        element,
        target: Date.parse(element.dataset.countdown),
        offset: Date.parse(element.dataset.now) - Date.now(),
    }));

    const tick = () => {
        for (const { element, target, offset } of timers) {
            const left = Math.max(0, Math.ceil((target - (Date.now() + offset)) / 1000));
            element.textContent = `${Math.floor(left / 3600)}:${pad(Math.floor((left % 3600) / 60))}:${pad(left % 60)}`;
            if (left === 0 && !reloading) {
                reloading = true;
                // Only if the countdown is still there: never reload a page the visitor has moved on to.
                setTimeout(() => element.isConnected && location.reload(), 3000 + Math.random() * 7000);
            }
        }
    };

    tick();
    setInterval(tick, 1000);

    // The reveal in the visitor's own time zone too (#176): <span data-reveal-local="{reveal}">, whose date and time as written
    // are midnight Eastern. Left empty when the visitor's clock says the same, because they're on Eastern time too.
    const language = (navigator.languages ?? []).find((tag) => tag.startsWith('en')) ?? 'en';
    for (const span of document.querySelectorAll('[data-reveal-local]')) {
        const reveal = new Date(Date.parse(span.dataset.revealLocal));
        const local = `${reveal.getFullYear()}-${pad(reveal.getMonth() + 1)}-${pad(reveal.getDate())}T${pad(reveal.getHours())}:${pad(reveal.getMinutes())}`;
        if (local !== span.dataset.revealLocal.slice(0, 16)) {
            const time = new Intl.DateTimeFormat(language, { weekday: 'long', hour: 'numeric', minute: '2-digit' }).format(reveal);
            span.textContent = ` (${time} your time)`;
        }
    }
})();
