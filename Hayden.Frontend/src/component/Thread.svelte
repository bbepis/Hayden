<script lang="ts">
	import { onMount } from "svelte";
	import type { ThreadModel, PostModel } from "../data/data";
	import Post from "./Post.svelte";

	interface Props {
		thread: ThreadModel;
		jumpToHash?: boolean;
	}

	let { thread, jumpToHash = false }: Props = $props();

	function calculateBackquotes(post: PostModel): number[] {
		return thread.posts
			.filter((x) => {
				return (
					(x.contentHtml &&
						x.contentHtml.indexOf(`&gt;&gt;${post.postId}`) >= 0) ||
					(x.contentRaw &&
						x.contentRaw.indexOf(`>>${post.postId}`) >= 0)
				);
			})
			.map((x) => x.postId);
	}

	onMount(() => {
		if (jumpToHash) {
			window.location.hash = window.location.hash;
		}
	});
</script>

<div class="thread">
	{#each thread.posts as post, index (post.postId)}
		<div class:reply-margin={index !== 0}>
			<Post
				{post}
				threadId={thread.threadId}
				board={thread.board}
				subject={index === 0 ? thread.subject : undefined}
				backquotes={calculateBackquotes(post)}
				onPostAction={postAction}
			/>
		</div>
	{/each}
</div>

<style>
	.reply-margin {
		margin-left: 25px;
	}
</style>
