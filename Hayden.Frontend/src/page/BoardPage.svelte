<script lang="ts">
    import { onMount } from "svelte";
    import PageSelector from "../component/PageSelector.svelte";
    import PostUploader from "../component/PostUploader.svelte";
    import Thread from "../component/Thread.svelte";
    import { Api } from "../data/api";
    import type { BoardPageModel } from "../data/data";

    let dataPromise: Promise<BoardPageModel>;

    export let initialCurrentPage = 1;
    export let board: string;

    let maxPage = initialCurrentPage;

    async function navigatePage(page: number) {
        if (page === 1) {
            document.location.href = `/board/${board}/`;
        }
        else {
            document.location.href = `/board/${board}/page/${page}/`;
        }
    }

    async function loadData() {
        dataPromise = Api.GetBoardPage(board, initialCurrentPage);

        var model = await dataPromise;

        maxPage = Math.ceil(model.totalThreadCount / 10);
    }

    loadData();

</script>

<style>
    .board-title {
        font-size: 30px;
        text-align: center;
        margin-bottom: 10px;
    }
</style>

{#await dataPromise}
    <p>Loading...</p>
{:then data}

    <div class="board-title">
        /{data.boardInfo.shortName}/ - {data.boardInfo.longName}
    </div>

    {#if data.boardInfo.isReadOnly === false}
        <PostUploader isThreadUploader={true} board={board} />
    {/if}

    {#each data.threads as thread ({a: thread.board.id, b: thread.threadId}) }
    
        <br/>
        <hr/>

        <Thread {thread} />
    {/each}
{:catch}
    <p>Error 1</p>
{/await}

<PageSelector currentPage={initialCurrentPage} {maxPage} on:page={event => navigatePage(event.detail.page)} />