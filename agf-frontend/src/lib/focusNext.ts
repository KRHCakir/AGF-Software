// Het volgende focusbare veld in hetzelfde formulier, zoals Tab dat zou kiezen (voor Enter op een suggestie of op een eenheid).
// Velden met tabIndex -1 (hulpknoppen voor de muis), uitgeschakelde velden en verborgen invoer tellen niet mee.
export function focusNextField(element: HTMLElement) {
    const scope: ParentNode = element.closest('form') ?? element.ownerDocument;
    const focusable = Array.from(scope.querySelectorAll<HTMLElement>('input, select, textarea, button, a[href]'))
        .filter((candidate) => candidate.tabIndex >= 0 && !(candidate as HTMLInputElement).disabled && (candidate as HTMLInputElement).type !== 'hidden');
    focusable[focusable.indexOf(element) + 1]?.focus();
}
