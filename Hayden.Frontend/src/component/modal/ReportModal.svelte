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

	export const showModal: (boardId: number, postId: number) => void = (
		boardId: number,
		postId: number,
	) => {
		setBoardId = boardId;
		setPostId = postId;
		modal?.show();
	};

	interface ICategory {
		value: number;
		text: string;
	}

	const reportCategories: ICategory[] = [
		{ value: 4, text: "CSAM / Child Pornography" },
		{ value: 4, text: "Illegal content" },
		{ value: 3, text: "DMCA / Copyright claim" },
		{ value: 2, text: "Doxx / Personal info" },
		{ value: 1, text: "Other" },
	];

	let category: ICategory = $state(reportCategories[4]);
	let additionalInfo: string = $state("");

	async function sendReport() {
		await Utility.PostForm("/makereport", {
			boardId: setBoardId,
			postId: setPostId,
			categoryLevel: category.value,
			additionalInfo: (category.text + "\n" + additionalInfo).trim(),
		});

		modal?.close();
	}

	let modal: Modal | undefined = $state();
</script>

{#snippet header(text: string)}
	<div class="mr-2 px-2 py-1 bg-box-header text-right content-center">{text}</div>
{/snippet}

<Modal bind:this={modal} title="Report post">
	<div class="grid grid-cols-[max\-content_1fr] gap-y-1">
		{@render header("Board")}
		<Textbox disabled value={boardInfo?.find(x => x.id == setBoardId)?.shortName ?? setBoardId?.toString()} />
		{@render header("Post number")}
		<Textbox disabled value={setPostId?.toString()} />
		{@render header("Category")}
		<select class="" bind:value={category}>
			{#each reportCategories as category}
				<option value={category}>
					{category.text}
				</option>
			{/each}
		</select>
		{@render header("Additional info")}
		<Textbox area bind:value={additionalInfo} />

		<div></div>
		<div class="ml-auto">
			<button onclick={sendReport}>Submit</button>
		</div>
	</div>
</Modal>