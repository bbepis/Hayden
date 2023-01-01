<script lang="ts">
	import { onMount } from "svelte";
	import type { ThreadModel, PostModel } from "../data/data";
    import BanUserModal from "./admin/BanUserModal.svelte";
    import DeletePostModal from "./admin/DeletePostModal.svelte";
	import Post from "./Post.svelte";

	export let thread: ThreadModel;
	export let jumpToHash: boolean = false;

	let banUserModal: BanUserModal;
	let deletePostModal: DeletePostModal;

	function postAction(
		e: CustomEvent<{ action: string; boardId: number; postId: number }>
	) {
		if (e.detail.action === "ban-ip") {
			banUserModal.showModal(e.detail.boardId, e.detail.postId);
		}
		else if (e.detail.action === "delete-post") {
			deletePostModal.showModal(e.detail.boardId, e.detail.postId);
		}
	}

	function calculateBackquotes(post: PostModel): number[] {
		return thread.posts
			.filter((x) => {
				return (x.contentHtml && x.contentHtml.indexOf(`&gt;&gt;${post.postId}`) >= 0)
					|| (x.contentRaw && x.contentRaw.indexOf(`>>${post.postId}`) >= 0);
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
				subject={index === 0 ? thread.subject : null}
				backquotes={calculateBackquotes(post)}
				on:postaction={postAction}
			/>
		</div>
	{/each}
</div>

<BanUserModal bind:this={banUserModal} />
<DeletePostModal bind:this={deletePostModal} />

<style>
	.reply-margin {
		margin-left: 25px;
	}
</style>