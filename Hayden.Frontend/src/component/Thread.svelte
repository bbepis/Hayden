<script lang="ts">
	import { onMount } from "svelte";
	import type { ThreadModel, PostModel } from "../data/data";
	import Post from "./Post.svelte";
	import cash from "cash-dom";
	import { postHoverStore } from "../data/stores";

	interface Props {
		thread: ThreadModel;
		jumpToHash?: boolean;
	}

	let { thread, jumpToHash = false }: Props = $props();

	function calculateBackquotes(): Record<number, number[]> {
		const backquotes: Record<number, number[]> = {};

		for (let post of thread.posts) {
			if (post.contentRaw && post.contentRaw.length > 0)
			{
				for (let match of post.contentRaw.matchAll(/>>(\d+)/g)) {
					let targetPostId = parseInt(match[1]);

					if (!(targetPostId in backquotes))
						backquotes[targetPostId] = [post.postId];
					else
						backquotes[targetPostId].push(post.postId);
				}
			}
		}

		return backquotes;
	}

	let allBackquotes = calculateBackquotes();

	let threadDiv: HTMLDivElement | undefined = $state();

	onMount(() => {
		setTimeout(() => {
			const quotelinks: { id: number, a: HTMLAnchorElement }[] = [];
			cash(threadDiv).find("a.quoteLink").each((i, e) => {
				if (e.dataset.postid) {
					quotelinks.push({ id: parseInt(e.dataset.postid), a: <HTMLAnchorElement>e });
				}
			});

			for (let link of quotelinks) {
				if (thread.posts.some(x => x.postId === link.id))
					link.a.href = `#/${thread.board.shortName}/thread/${thread.threadId}?post=${link.id}`;
				else
					link.a.href = `#/${thread.board.shortName}/post/${link.id}`;


				link.a.addEventListener("mouseenter", () => {
					postHoverStore.set({ boardId: thread.board.id, postId: link.id });
				});
				link.a.addEventListener("mouseleave", () => {
					postHoverStore.set(undefined);
				});
				link.a.addEventListener("click", e => {
					const targetPost = cash(document).find(`div[data-postid="${link.id}"][data-boardid="${thread.board.id}"]`);

					if (targetPost.length > 0) {
						targetPost[0]?.scrollIntoView({ behavior: "smooth" });
						e.preventDefault();
					}
					else {
						postHoverStore.set(undefined);
					}
				});
			}
		}, 10);

		if (jumpToHash) {
			window.location.hash = window.location.hash;
		}
	});
</script>

<div class="thread" bind:this={threadDiv}>
	{#each thread.posts as post, index (post.postId)}
		<div class:reply-margin={index !== 0}>
			<Post
				{post}
				board={thread.board}
				subject={index === 0 ? thread.subject : undefined}
				backquotes={allBackquotes[post.postId]}
			/>
		</div>
	{/each}
</div>

<style>
	.reply-margin {
		margin-left: 25px;
	}
</style>
