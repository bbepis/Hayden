<script lang="ts">
	import { createEventDispatcher } from 'svelte';
    import type { PostModel } from "../data/data";

	const dispatch = createEventDispatcher();

    export let boardId: number;
    export let postId: number;

    function showDeletePostModal() {
        dispatch('postaction', {
            action: 'delete-post',
			boardId: boardId,
            postId: postId
		});
    }

    function showBanIpModal() {
        dispatch('postaction', {
            action: 'ban-ip',
			boardId: boardId,
            postId: postId
		});
    }
</script>

<div class="menu">
    <div class="menu-item" on:click={showDeletePostModal}>
        Delete post
    </div>
    <!-- <div class="menu-item">
        Delete image
    </div> -->
    <div class="menu-item" on:click={showBanIpModal}>
        Ban poster IP
    </div>
</div>

<style>
    .menu {
        width: 150px;
        background-color: #282a2e;
    }

    .menu-item {
        border: 1px solid #555;
        cursor: pointer;
        user-select: none;
    }
</style>
