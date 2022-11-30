<script lang="ts">
    import { onMount } from "svelte";
    import PageSelector from "../component/PageSelector.svelte";
    import Thread from "../component/Thread.svelte";
    import { Api } from "../data/api";
    import type { BoardPageModel } from "../data/data";
    import { Utility } from "../data/utility";

    let dataPromise: Promise<BoardPageModel>;

    export let initialCurrentPage = 1;
    export let board: string;

    let maxPage = initialCurrentPage;

    async function navigatePage(page: number) {
        if (page === 1) {
            document.location.href = `/board/${board}/`;
        }
        else {
            document.location.href = `/board/${board}/page/${page}/`;
        }
    }

    async function loadData() {
        dataPromise = Api.GetBoardPage(board, initialCurrentPage);

        var model = await dataPromise;

        maxPage = Math.ceil(model.totalThreadCount / 10);

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

    loadData();

    
    let formName: string = null;
    let formText: string = null;
    let formCaptcha: string = null;
    let formSubject: string = null;
    let formFiles: FileList = null;
    let isPosting = false;

    async function Post() {
        if ((formText === "" || formText === null) && (formFiles === null || formFiles.length === 0))
            return;

        if (formCaptcha == null)
            return;

        if (isPosting)
            return;

        isPosting = true;

        const postObject = {
            name: formName,
            text: formText,
            subject: formSubject,
            file: formFiles !== null ? formFiles[0] : null,
            captcha: formCaptcha,
            board: board
        };

        try {
            const response = await Utility.PostForm("/makethread", postObject);

            console.log(response);

            if (response.ok)
            {
                let obj = await response.json();
                document.location.href = `/${board}/thread/${obj.threadId}`;
            }
        }
        catch (e) {
            console.log(e);
        }

        isPosting = false;
    }

</script>

<style>
    #reply-box {
        width: 600px;
        /* background-color: #444; */
        background-color: var(--post-background-color);
        /* color: white; */
        color: var(--text-color);
        border-color: var(--post-border-color) !important;
    }

    .input-row {
        padding: 5px 0px;
    }

    .board-title {
        font-size: 30px;
        text-align: center;
        margin-bottom: 10px;
    }
</style>

{#await dataPromise}
    <p>Loading...</p>
{:then data}

    <div class="board-title">
        /{data.boardInfo.shortName}/ - {data.boardInfo.longName}
    </div>

    {#if data.boardInfo.isReadOnly === false}
        <div id="reply-box" class="rounded border mb-5 container">
            <div style="text-align: center">Create a new thread</div>
            <div class="row input-row">
                <div class="col-3">Name</div>
                <div class="col-9"><input class="w-100" type="text" bind:value={formName} /></div>
            </div>
            <div class="row input-row">
                <div class="col-3">Subject</div>
                <div class="col-9"><input class="w-100" type="text" bind:value={formSubject} /></div>
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
                <button on:click={Post} class="mx-3 form-control btn btn-outline-secondary">Create Thread</button>
            </div>
        </div>
    {/if}

    {#each data.threads as thread ({a: thread.board.id, b: thread.threadId}) }
    
        <br/>
        <hr/>

        <Thread {thread} />
    {/each}
{:catch}
    <p>Error 1</p>
{/await}

<PageSelector currentPage={initialCurrentPage} {maxPage} on:page={event => navigatePage(event.detail.page)} />