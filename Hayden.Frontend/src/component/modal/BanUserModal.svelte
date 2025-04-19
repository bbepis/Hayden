<script lang="ts">
	import type { BoardModel } from "../../data/data";
	import { boardInfoStore } from "../../data/stores";
	import { Utility } from "../../data/utility";
	import Modal from "../form/Modal.svelte";
	import Textbox from "../form/Textbox.svelte";

	let boardInfo: BoardModel[] | undefined = $state();
	(async () => boardInfo = await $boardInfoStore)();

    let setBoardId: number = $state(0);
    let setPostId: number = $state(0);
    let reasonPrivate: string = $state("");
    let reasonPublic: string = $state("");
    let hoursBan: number = $state(1);
    let permanent: boolean = $state(false);

	export const showModal: (boardId: number, postId: number) => void = (
		boardId: number,
		postId: number,
	) => {
		setBoardId = boardId;
		setPostId = postId;
		reasonPrivate = "";
		reasonPublic = "";
		hoursBan = 1;
		permanent = false;
		modal?.show();
	};

    async function sendBan() {
        await Utility.PostForm("/moderator/banuser", {
            boardId: setBoardId,
            postId: setPostId,
            seconds: hoursBan * 3600,
            indefinite: permanent,
            internalReason: reasonPrivate,
            publicReason: reasonPublic
        });

		modal?.close();
	}

	let modal: Modal | undefined = $state();
</script>

{#snippet header(text: string)}
	<div class="mr-2 px-2 py-1 bg-box-header text-right content-center">{text}</div>
{/snippet}

<Modal bind:this={modal} title="Ban User">
	<div class="grid grid-cols-[max\-content_1fr] gap-y-1">
		{@render header("Board")}
		<Textbox disabled value={boardInfo?.find(x => x.id == setBoardId)?.shortName ?? setBoardId?.toString()} />
		{@render header("Post number")}
		<Textbox disabled value={setPostId?.toString()} />
		{@render header("Reason (private)")}
		<Textbox bind:value={reasonPrivate} />
		{@render header("Reason (public)")}
		<Textbox bind:value={reasonPublic} />
		{@render header("Hours")}
		<input
			type="number"
			class="w-full h-full textbox-container focus:outline-none focus:border-highlight! rounded px-1 py-1"
			min="1"
			disabled={permanent}
			bind:value={hoursBan}
		/>
		{@render header("")}
		<div>
			<input id="input-permanent-ban" type="checkbox" class="accent-highlight" bind:checked={permanent} />
			<label for="input-permanent-ban">Permanent ban</label>
		</div>

		<div></div>
		<div class="ml-auto">
			<button onclick={sendBan}>Ban</button>
		</div>
	</div>
</Modal>