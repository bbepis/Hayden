<script lang="ts">
	import type { BoardModel } from "../../data/data";
	import { boardInfoStore } from "../../data/stores";
	import { Utility } from "../../data/utility";
	import Modal from "../form/Modal.svelte";
	import Textbox from "../form/Textbox.svelte";

	let boardInfo: BoardModel[] | undefined = $state();
	(async () => boardInfo = await $boardInfoStore)();

	let setBoardId: number | undefined = $state();
	let setPostId: number | undefined = $state();
    let banImages: boolean = $state(false);
	let additionalInfo: string = $state("");

	export const showModal: (boardId: number, postId: number) => void = (
		boardId: number,
		postId: number,
	) => {
		setBoardId = boardId;
		setPostId = postId;
		modal?.show();
	};

    async function deletePost() {
        await Utility.PostForm("/moderator/deletepost", {
            boardId: setBoardId,
            postId: setPostId,
            banImages: banImages
        });

		modal?.close();
	}

	let modal: Modal | undefined = $state();
</script>

{#snippet header(text: string)}
	<div class="mr-2 px-2 py-1 bg-box-header text-right content-center">{text}</div>
{/snippet}

<Modal bind:this={modal} title="Delete post">
	<div class="grid grid-cols-[max\-content_1fr] gap-y-1">
		{@render header("Board")}
		<Textbox disabled value={boardInfo?.find(x => x.id == setBoardId)?.shortName ?? setBoardId?.toString()} />
		{@render header("Post number")}
		<Textbox disabled value={setPostId?.toString()} />
		{@render header("")}
		<div>
			<input id="ban-images" type="checkbox" class="accent-highlight" bind:checked={banImages} />
			<label for="ban-images">Ban images</label>
		</div>
		{@render header("Additional info")}
		<Textbox area bind:value={additionalInfo} />

		<div></div>
		<div class="ml-auto">
			<button onclick={deletePost}>Delete</button>
		</div>
	</div>
</Modal>