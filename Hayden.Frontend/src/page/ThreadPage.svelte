<script lang="ts">
    import Thread from "../component/Thread.svelte";
    import type { ThreadModel } from "../data/data";
    import { Utility } from "../data/utility";


    export let board: string;
    export let threadId: number;

    let dataPromise: Promise<ThreadModel> = Utility.FetchData(`/${board}/thread/${threadId}`);
</script>

{#await dataPromise}
    <p>Loading...</p>
{:then thread} 
    <Thread {thread} jumpToHash={true} />
{:catch}
    <p>Error</p>
{/await}
