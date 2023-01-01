// https://svelte.dev/repl/0ace7a508bd843b798ae599940a91783?version=3.16.7
/** Dispatch event on click outside of node */
export function clickOutside(node: HTMLElement) {
    const handleClick = event => {
        if (node && !node.contains(event.target) && !event.defaultPrevented) {
            node.dispatchEvent(
                new CustomEvent('click_outside', {detail: node})
            )
        }
    }

    document.addEventListener('mousedown', handleClick, true);

    return {
        destroy() {
            document.removeEventListener('mousedown', handleClick, true);
        }
    }
}