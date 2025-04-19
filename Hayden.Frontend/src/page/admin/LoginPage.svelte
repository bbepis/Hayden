<script lang="ts">
    import { moderatorUserStore } from "../../data/stores"
    import { Api } from "../../data/api";
	import Textbox from "../../component/form/Textbox.svelte";
	import { Utility } from "../../data/utility";
	import { push } from "svelte-spa-router";


    let formUsername: string = $state("");
    let formPassword: string = $state("");
    let error: string | undefined = $state(undefined);

    async function login() {
        if (!Utility.IsNotEmpty(formUsername) || !Utility.IsNotEmpty(formPassword))
            return;

        const result = await Api.UserLoginAsync(formUsername, formPassword);

        const userInfo = await Api.GetUserInfoAsync();

        $moderatorUserStore = userInfo.role;

        if (result) {
            error = undefined;
            push("/");
        }
        else {
            error = "Invalid login";
        }
    }
</script>

{#snippet header(text: string)}
	<div class="mr-2 px-2 py-1 bg-box-header text-right content-center">{text}</div>
{/snippet}

<div class="flex justify-center w-full">
	<div>
		<div class="text-xl font-bold text-center mb-1">Admin login</div>
		<div class="bg-post-bg postborder min-w-[500px] grid grid-cols-[max\-content_1fr] gap-y-1 p-2">
			{@render header("Username")}
			<Textbox bind:value={formUsername} />
			{@render header("Password")}
			<Textbox bind:value={formPassword} />
			<div></div>
			<div class="ml-auto">
				<button onclick={login}>Submit</button>
			</div>
		</div>
	</div>
</div>

{#if error}
    <p>{error}</p>
{/if}
