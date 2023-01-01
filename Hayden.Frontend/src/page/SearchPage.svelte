<script lang="ts">
    import Thread from "../component/Thread.svelte";
    import type { BoardPageModel, ThreadModel } from "../data/data";
    import { Utility } from "../data/utility";

    // const urlSearchParams = new URLSearchParams(window.location.search);
    // console.log(urlSearchParams);
    // let query: string = urlSearchParams.get("query");
    // console.log(query);

    import { meta } from 'tinro';
    import { onDestroy } from "svelte";
    const route = meta();
    let query = route.query["query"];

    const unsubscribe = route.subscribe(m => {
        if (m && m.query["query"] !== query
            && m.query["query"] !== undefined
            && m.query["query"] !== "") {
            query = m.query["query"];
            search();
        }
    });

    onDestroy(unsubscribe);

    let dataPromise: Promise<BoardPageModel> = null;

    function search() {
        dataPromise = Utility.FetchData("/search", {
            query: query
        });
    }

    if (query != null) {
        search();
    }
</script>

<!-- <input bind:value={query} on:keyup={enterHandler}>
<button on:click={search}>Search</button> -->

{#if dataPromise}
    {#await dataPromise}
        <p>Loading...</p>
    {:then data} 
        {#each data.threads as thread ([thread.board.id, thread.threadId]) }
        
            <br/>
            <hr/>

            <Thread {thread} />
        {/each}
    {:catch}
        <p>Error 2</p>
    {/await}
{/if}