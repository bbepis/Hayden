<script lang="ts">
    import Thread from "../component/Thread.svelte";
    import type { ThreadModel } from "../data/data";
    import { Utility } from "../data/utility";


    let query: string = "";
    let dataPromise: Promise<ThreadModel[]> = null;

    function search() {
        dataPromise = Utility.FetchData("/search", {
            query: query
        });
    }

    function enterHandler(event: KeyboardEvent) {
        if (event.key === "Enter") {
            event.preventDefault();
            search();
        }
    }
</script>

<input bind:value={query} on:keyup={enterHandler}>
<button on:click={search}>Search</button>

{#if dataPromise}
    {#await dataPromise}
        <p>Loading...</p>
    {:then data} 
        {#each data as thread ([thread.board.id, thread.threadId]) }
        
            <br/>
            <hr/>

            <Thread {thread} />
        {/each}
    {:catch}
        <p>Error 2</p>
    {/await}
{/if}