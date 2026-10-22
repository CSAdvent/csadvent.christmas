// Copy buttons: <button data-copy="element-id"> copies that element's text to the clipboard.
document.addEventListener("click", async (event) => {
    const button = event.target.closest("[data-copy]");
    const source = button && document.getElementById(button.dataset.copy);
    if (!source) {
        return;
    }

    await navigator.clipboard.writeText(source.textContent);
    const label = button.textContent;
    button.textContent = "Copied!";
    setTimeout(() => { button.textContent = label; }, 1500);
});
