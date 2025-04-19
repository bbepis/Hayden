<script lang="ts">
	import { moderatorUserStore } from "../../data/stores";
	import { Api } from "../../data/api";
	import Textbox from "../../component/form/Textbox.svelte";
	import { push } from "svelte-spa-router";

	let formUsername: string = $state("");
	let formPassword: string = $state("");
	let formRegisterCode: string = $state("");
	let error: string | null = $state("");

	async function register() {
		if (
			formUsername == null ||
			formPassword == null ||
			formRegisterCode == null
		)
			return;

		const result = await Api.UserRegisterAsync(
			formUsername,
			formPassword,
			formRegisterCode,
		);

		if (result.success) {
			error = null;

			const userInfoResult = await Api.GetUserInfoAsync();
			$moderatorUserStore = userInfoResult.role;

			push("/");
		} else {
			$moderatorUserStore = null;
			error = result.error;
		}
	}
</script>

{#snippet header(text: string)}
	<div class="mr-2 px-2 py-1 bg-box-header text-right content-center">{text}</div>
{/snippet}

<div class="flex justify-center w-full">
	<div>
		<div class="text-xl font-bold text-center mb-1">Admin registration</div>
		<div class="bg-post-bg postborder min-w-[500px] grid grid-cols-[max\-content_1fr] gap-y-1 p-2">
			{@render header("Username")}
			<Textbox bind:value={formUsername} />
			{@render header("Password")}
			<Textbox bind:value={formPassword} />
			{@render header("Register code")}
			<Textbox bind:value={formRegisterCode} />
			<div></div>
			<div class="ml-auto">
				<button onclick={register}>Submit</button>
			</div>
		</div>
	</div>
</div>

{#if error}
	<p style="color: red">{error}</p>
{/if}