<script lang="ts">
	import { Utility } from "../../data/utility";

	let setBoardId: number;
	let setPostId: number;

	export const showModal: (boardId: number, postId: number) => void = (
		boardId: number,
		postId: number,
	) => {
		setBoardId = boardId;
		setPostId = postId;
		jQuery(banUserModal).modal();
	};

	interface ICategory {
		value: number;
		text: string;
	}

	let category: ICategory = null;
	let additionalInfo: string = "";

	const reportCategories: ICategory[] = [
		{ value: 4, text: "CSAM / Child Pornography" },
		{ value: 4, text: "Illegal content" },
		{ value: 3, text: "DMCA / Copyright claim" },
		{ value: 2, text: "Doxx / Personal info" },
		{ value: 1, text: "Other" },
	];

	async function sendReport() {
		await Utility.PostForm("/makereport", {
			boardId: setBoardId,
			postId: setPostId,
			categoryLevel: category.value,
			additionalInfo: (category.text + "\n" + additionalInfo).trim(),
		});

		jQuery(banUserModal).modal("hide");
	}

	let banUserModal: HTMLDivElement;
</script>

<div
	bind:this={banUserModal}
	class="modal fade"
	tabindex="-1"
	role="dialog"
	aria-labelledby="exampleModalLabel"
	aria-hidden="true"
>
	<div class="modal-dialog" role="document">
		<div class="modal-content">
			<div class="modal-header">
				<h5 class="modal-title" id="exampleModalLabel">Report post</h5>
				<button
					type="button"
					class="close"
					data-dismiss="modal"
					aria-label="Close"
				>
					<span aria-hidden="true">&times;</span>
				</button>
			</div>
			<div class="modal-body">
				<div class="container">
					<div class="row my-1">
						<div class="col-4">Category:</div>
						<div class="col-8">
							<select class="form-control" bind:value={category}>
								{#each reportCategories as category}
									<option value={category}>
										{category.text}
									</option>
								{/each}
							</select>
						</div>
					</div>
					<div class="row my-1">
						<div class="col-4">Additional info:</div>
						<div class="col-8">
							<textarea
								class="form-control"
								bind:value={additionalInfo}
							/>
						</div>
					</div>
				</div>
			</div>
			<div class="modal-footer">
				<button
					type="button"
					class="btn btn-secondary"
					data-dismiss="modal"
				>
					Close
				</button>
				<button
					type="button"
					class="btn btn-primary"
					on:click={sendReport}
				>
					Send
				</button>
			</div>
		</div>
	</div>
</div>

<style>
	.modal {
		color: #212529;
	}
</style>
