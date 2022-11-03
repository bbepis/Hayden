<script lang="ts">
    import Thread from "../component/Thread.svelte";
    import type { ThreadModel } from "../data/data";
    import { Utility } from "../data/utility";

    let dataPromise: Promise<ThreadModel[]> = Utility.FetchData("/index");
</script>

<div class="container-margin">
    {#await dataPromise}
        <p>Loading...</p>
    {:then data}
        {#each data as thread ({a: thread.board.id, b: thread.threadId}) }
        
            <br/>
            <hr/>

            <Thread {thread} />
        {/each}
    {:catch}
        <p>Error 1</p>
    {/await}
</div>