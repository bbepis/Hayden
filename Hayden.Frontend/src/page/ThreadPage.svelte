<script lang="ts">
    import Thread from "../component/Thread.svelte";
    import type { ThreadModel } from "../data/data";
    import { Utility } from "../data/utility";
    import { moderatorUserStore } from "../data/stores";
    import { post } from "jquery";


    export let board: string;
    export let threadId: number;

    let thread: ThreadModel = null;
    let errorOccurred: Boolean = false;

    let formName: string = null;
    let formText: string = null;
    let formFiles: FileList = null;
    let formCaptcha: string = null;

    let postErrorMessage: string = null;

    let isRefreshing: Boolean = false;
    let hasLoadedSuccessfullyOnce: Boolean = false;

    async function FetchThread() {
        try {
            thread = <ThreadModel>(await Utility.FetchData(`/${board}/thread/${threadId}`));
            errorOccurred = false;

            if (!hasLoadedSuccessfullyOnce) {
                setTimeout(() => {
                    (<any>window).hcaptcha.render("post-captcha", {
                        theme: "dark",
                        callback: (response) => { formCaptcha = response },
                        "expired-callback": () => { formCaptcha = null },
                        "close-callback": () => { formCaptcha = null },
                        "error-callback": () => { formCaptcha = null },
                    });
                }, 500)
            }

            hasLoadedSuccessfullyOnce = true;
        }
        catch {
            errorOccurred = true;
        }
    }

    async function Refresh() {
        isRefreshing = true;
        await FetchThread();
        isRefreshing = false;
    }

    let isPosting = false;

    async function Post() {

        if ((formText === "" || formText === null) && (formFiles === null || formFiles.length === 0))
            return;

        if (isPosting)
            return;

        isPosting = true;

        const postObject = {
            name: formName,
            text: formText,
            file: formFiles !== null ? formFiles[0] : null,
            captcha: formCaptcha,
            board: board,
            threadId: threadId
        };

        console.log(postObject);

        try {
            const response = await Utility.PostForm("/makepost", postObject);

            console.log(response);

            if (response.ok)
            {
                Refresh();
                formName = null;
                formText = null;
                formFiles = null;
                postErrorMessage = null;
            }
            else {
                var obj = await response.json();
                
                if (obj["message"] !== undefined)
                    postErrorMessage = obj["message"];
            }
        }
        catch (e) {
            console.log(e);
        }

        isPosting = false;
    }

    FetchThread();
</script>

<style>
    #reply-box {
        max-width: 700px;
        /* background-color: #444; */
        background-color: var(--post-background-color);
        /* color: white; */
        color: var(--text-color);
        border-color: var(--post-border-color) !important;
    }

    .input-row {
        padding: 5px 0px;
    }
</style>

<div class="container-margin">

    {#if errorOccurred}
        <p>Error</p>
    {:else if thread === null}
        <p>Loading...</p>
    {:else}
        <Thread {thread} jumpToHash={true} />

        {#if thread.board.isReadOnly === false && thread.archived === false}
            <div id="reply-box" class="rounded border mb-5 container">
                {#if postErrorMessage !== null}
                <div class="row input-row">
                    <div class="col-12" style="color:red;">{postErrorMessage}</div>
                </div>
                {/if}
                <div class="row input-row">
                    <div class="col-3">Name</div>
                    <div class="col-9"><input class="w-100" type="text" bind:value={formName} /></div>
                </div>
                <div class="row input-row">
                    <div class="col-3">Comment</div>
                    <div class="col-9"><textarea class="w-100" bind:value={formText}></textarea></div>
                </div>
                <div class="row input-row">
                    <div class="col-3">File</div>
                    <div class="col-9"><input type="file" bind:files={formFiles} /></div>
                </div>
                <div class="row input-row">
                    <div class="col-3">Captcha</div>
                    <div class="col-9">
                        <div id="post-captcha" class="h-captcha" data-sitekey={Utility.infoObject.hCaptchaSiteKey}></div>
                    </div>
                </div>
                <div class="row input-row">
                    <button on:click={Post} class="mx-3 form-control btn btn-outline-secondary">Reply</button>
                </div>
            </div>
        {/if}

        {#if isRefreshing}
            <p>Refreshing...</p>
        {/if}

        <button class="btn btn-outline-secondary" on:click={Refresh}>Refresh</button>
    {/if}

</div>