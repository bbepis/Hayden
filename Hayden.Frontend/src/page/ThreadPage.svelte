<script lang="ts">
    import Thread from "../component/Thread.svelte";
    import { ThreadModel, Utility } from "../data";

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
