
<script lang="ts">
    import { createEventDispatcher } from 'svelte';
    import { Utility } from '../data/utility';

	const dispatch = createEventDispatcher();

    function goToPageHandler(page: number) : (e : Event) => void {
        return function (e : Event) {
            if (e) {
                e.preventDefault()
            }

            if (page < 0 || page > maxPage) {
                return;
            }

            //currentPage = page;
            dispatch('page', {
                page: page
            });
        }
    }

    export let currentPage : number;
    export let maxPage : number;
</script>

<!-- svelte-ignore a11y-invalid-attribute -->
<div class="justify-content-center">
    <ul class="pagination pagination-lg justify-content-center">
        <li class="page-item" class:disabled={currentPage <= 1}><a tinro-ignore class="page-link" on:click={goToPageHandler(currentPage - 1)} href="#">Previous</a></li>

        {#if currentPage > 2}
            <li class="page-item"><a tinro-ignore class="page-link" on:click={goToPageHandler(1)} href="#">1</a></li>
        {/if}

        {#if currentPage > 3}
            <li class="page-item gotopage-item disabled">
                <a tinro-ignore id="gotopage-link1" class="page-link" href="#">...</a>
                <form id="gotopage-form1" class="d-none page-link">
                    <input type="number" name="pageno" class="gotopage-input" />
                </form>
            </li>
            {/if}

        {#each Utility.RangeTo(currentPage - 1, currentPage + 2) as i}
            {#if !(i < 1 || i > maxPage)}
            <li class="page-item" class:active={i === currentPage}><a tinro-ignore class="page-link" on:click={goToPageHandler(i)} href="#">{i}</a></li>
            {/if}
        {/each}

        {#if currentPage < maxPage - 2}
            <li class="page-item gotopage-item disabled">
                <a tinro-ignore id="gotopage-link2" class="page-link" href="#">...</a>
                <form id="gotopage-form2" class="d-none page-link">
                    <input type="number" name="pageno" class="gotopage-input" />
                </form>
            </li>
        {/if}

        {#if currentPage < maxPage - 1}
            <li class="page-item"><a class="page-link" on:click={goToPageHandler(maxPage)} href="#">{maxPage}</a></li>
            {/if}

        <li class="page-item" class:disabled={currentPage >= maxPage}><a tinro-ignore class="page-link" on:click={goToPageHandler(currentPage + 1)} href="#">Next</a></li>
    </ul>
</div>