<script lang="ts">
	import cash from "cash-dom";
	import type { BoardModel, PostModel } from "../data/data";
	import { boardInfoStore, postHoverStore } from "../data/stores";
	import { Utility } from "../data/utility";
	import { onDestroy } from "svelte";
	import Post from "./Post.svelte";


	let show = $state(false);

	let left = $state(0);
	let top = $state(0);

	function mouseMove(e: MouseEvent) {
		left = e.pageX + 20;
		top = e.pageY;
	}

	let lastPost: { boardId: number, postId: number, boardInfo: BoardModel } | undefined = undefined;

	let postDataTask: Promise<PostModel> | undefined = $state();

	function isScrolledIntoView(el: HTMLElement | any) {
		var rect = el.getBoundingClientRect();
		var elemTop = rect.top;
		var elemBottom = rect.bottom;

		// Only completely visible elements return true:
		var isVisible = (elemTop >= 0) && (elemBottom <= window.innerHeight);
		// Partially visible elements return true:
		//isVisible = elemTop < window.innerHeight && elemBottom >= 0;
		return isVisible;
	}

	async function retrievePost(board: string, id: number): Promise<PostModel> {
		return <PostModel>(await Utility.FetchData(`/${board}/post/${id}`))
	}

	async function tryShowPost(boardId: number, postId: number) {
		const targetPost = cash(document).find(`div[data-postid="${postId}"][data-boardid="${boardId}"]`);

		if (targetPost.length > 0) {
			if (isScrolledIntoView(targetPost[0])) {
				// we can already see it, so there's no point showing a hover version
				show = false;
				return;
			}
		}

		if (!lastPost || lastPost.boardId !== boardId || lastPost.postId !== postId) {
			const boardInfo = await $boardInfoStore;

			postDataTask = retrievePost(boardInfo!.find(x => x.id === boardId)!.shortName, postId);
			lastPost = { boardId, postId, boardInfo };
		}

		show = true;
	}

	const unsub = postHoverStore.subscribe(x => {
		if (!x) {
			show = false;
			return;
		}

		tryShowPost(x.boardId, x.postId);
	});

	onDestroy(() => unsub());

</script>

<svelte:window onmousemove={mouseMove} />

{#if show && postDataTask}
	<div class="absolute" style="left: {left}px; top: {top}px;">
		{#await postDataTask}
			Loading post...
		{:then postData}
			<Post board={lastPost!.boardInfo} post={postData} />
		{:catch}
			Could not load post
		{/await}
	</div>
{/if}