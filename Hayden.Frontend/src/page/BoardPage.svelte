<script lang="ts">
    import PageSelector from "../component/PageSelector.svelte";
    import PostUploader from "../component/PostUploader.svelte";
    import Thread from "../component/Thread.svelte";
    import { Api } from "../data/api";
    import type { BoardPageModel } from "../data/data";
	import { push } from "svelte-spa-router";

	interface Props {
		params: {
			page?: string;
			board: string;
		};
	}

	let { params }: Props = $props();

    let dataPromise: Promise<BoardPageModel> | undefined = $state();

	let currentPage = $state(!!params.page ? parseInt(params.page) : 1);
    let maxPage = $state(currentPage);

    async function navigatePage(page: number) {
        let newUrl;

        if (page === 1) {
            newUrl = `/${params.board}/`;
        }
        else {
            newUrl = `/${params.board}/page/${page}/`;
        }

        push(newUrl);
    }

	let viewingBoard: string | undefined = undefined;
	let viewingPage: number | undefined = undefined;

    async function loadData(board: string, page: number) {
		viewingBoard = board;
		viewingPage = page;
        dataPromise = Api.GetBoardPage(board, page);

        const model = await dataPromise;

        maxPage = Math.ceil(model.totalThreadCount / 10);
    }

	function reloadData(paramPage: number) {
		if (viewingBoard == params.board && viewingPage == paramPage)
			return;

		loadData(params.board, paramPage);
		currentPage = paramPage;
	}

	$effect(() => {
		let paramPage = !!params.page ? parseInt(params.page) : 1;

		// causes an infinite effect loop without this
		setTimeout(() => {
			reloadData(paramPage);
		}, 1);
	});

    loadData(params.board, currentPage);

</script>

{#await dataPromise}
    <p>Loading...</p>
{:then data}

	{#if data}
		<div class="board-title">
			/{data.boardInfo.shortName}/ - {data.boardInfo.longName}
		</div>

		{#if data.boardInfo.isReadOnly === false}
			<PostUploader isThreadUploader={true} board={params.board} />
		{/if}

		{#each data.threads as thread, index ([thread.board.id, thread.threadId]) }

			<br/>
			<hr/>

			<Thread {thread} />
		{/each}

		<PageSelector currentPage={currentPage} {maxPage} pageCallback={navigatePage} />
	{/if}
{:catch}
    <p>Error 1</p>
{/await}

<style>
    .board-title {
        font-size: 30px;
        text-align: center;
        margin-bottom: 10px;
    }
</style>