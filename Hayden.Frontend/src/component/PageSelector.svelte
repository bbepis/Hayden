
<script lang="ts">
    import { Utility } from '../data/utility';

    function goToPageHandler(page: number) : (e : Event) => void {
        return function (e : Event) {
            if (e) {
                e.preventDefault()
            }

            if (page < 0 || page > maxPage) {
                return;
            }

			pageCallback(page);
        }
    }

    interface Props {
        currentPage: number;
        maxPage: number;
		pageCallback: (page: number) => void;
    }

    let { currentPage, maxPage, pageCallback }: Props = $props();
</script>

<!-- svelte-ignore a11y_invalid_attribute -->
<div class="flex justify-center text-lg gap-x-2">
	<button disabled={currentPage <= 1} onclick={goToPageHandler(currentPage - 1)}>Previous</button>

	{#if currentPage > 2}
		<button onclick={goToPageHandler(1)}>1</button>
	{/if}

	{#if currentPage > 3}
		<button disabled>...</button>
	{/if}

	{#each Utility.RangeTo(currentPage - 1, currentPage + 2) as i}
		{#if !(i < 1 || i > maxPage)}
			<button class:active={i === currentPage} onclick={goToPageHandler(i)}>{i}</button>
		{/if}
	{/each}

	{#if currentPage < maxPage - 2}
		<button disabled>...</button>
	{/if}

	{#if currentPage < maxPage - 1}
		<button onclick={goToPageHandler(maxPage)}>{maxPage}</button>
	{/if}

	<button disabled={currentPage >= maxPage} onclick={goToPageHandler(currentPage + 1)}>Next</button>
</div>

<style>
	.active {
		background-color: var(--selected-color);
		border: 1px solid var(--color-highlight);
	}
</style>