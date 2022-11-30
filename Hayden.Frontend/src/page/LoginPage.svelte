<script lang="ts">
    import { moderatorUserStore } from "../data/stores"
    import { Api } from "../data/api";


    let formUsername: string = null;
    let formPassword: string = null;
    let error: string | null = null;

    async function login() {
        if (formUsername == null || formPassword == null)
            return;

        const result = await Api.UserLoginAsync(formUsername, formPassword);

        const userInfo = await Api.GetUserInfoAsync();

        $moderatorUserStore = userInfo.role;

        console.log(result);

        if (result) {
            error = null;
            document.location.href = "/";
        }
        else {
            error = "Invalid login";
        }
    }
</script>


{#if error}
    <p>{error}</p>
{/if}

<div id="reply-box" class="rounded border mb-5 container">
    <div class="row input-row">
        <div class="col-3">Username</div>
        <div class="col-9"><input class="w-100" type="text" bind:value={formUsername} /></div>
    </div>
    <div class="row input-row">
        <div class="col-3">Password</div>
        <div class="col-9"><input class="w-100" type="password" bind:value={formPassword} /></div>
    </div>
    <div class="row input-row">
        <button on:click={login} class="mx-3 form-control btn btn-outline-secondary">Login</button>
    </div>
</div>