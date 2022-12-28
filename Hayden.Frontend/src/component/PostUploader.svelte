<script lang="ts">
    import { createEventDispatcher, onMount } from "svelte";
    import { Utility } from "../data/utility";

    const dispatch = createEventDispatcher();

    export let board: string;
    export let threadId: number = null;
    export let isThreadUploader: boolean;

    let formName: string = null;
    let formText: string = null;
    let formCaptcha: string = null;
    let formSubject: string = null;
    let formFiles: FileList = null;
    let isPosting = false;

    let showForm = !isThreadUploader;

    let postErrorMessage: string = null;

    async function UploadThread(): Promise<{ message?: string }> {
        const postObject = {
            name: formName,
            text: formText,
            subject: formSubject,
            file: formFiles !== null ? formFiles[0] : null,
            captcha: formCaptcha,
            board: board,
        };

        const response = await Utility.PostForm("/makethread", postObject);

        if (response.ok) {
            (<any>window).hcaptcha.reset();

            let obj = await response.json();
            document.location.href = `/${board}/thread/${obj.threadId}`;

            return null;
        } else {
            var obj = await response.json();

            return obj;
        }
    }

    async function UploadPost(): Promise<{ message?: string }> {
        const postObject = {
            name: formName,
            text: formText,
            file: formFiles !== null ? formFiles[0] : null,
            captcha: formCaptcha,
            board: board,
            threadId: threadId,
        };

        const response = await Utility.PostForm("/makepost", postObject);

        if (response.ok) {
            (<any>window).hcaptcha.reset();

            formName = null;
            formText = null;
            formFiles = null;
            postErrorMessage = null;
        } else {
            var obj = await response.json();

            return obj;
        }
    }

    async function Post() {
        if (
            isThreadUploader &&
            (formFiles === null || formFiles.length === 0)
        ) {
            postErrorMessage = "You must have an attached file.";
            return;
        }

        if (
            (formText === "" || formText === null) &&
            (formFiles === null || formFiles.length === 0)
        ) {
            if (isThreadUploader) {
                postErrorMessage = "You must have an attached file.";
            } else {
                postErrorMessage = "You must have text or an attached file.";
            }

            return;
        }

        if (Utility.infoObject.maxGlobalUploadSize
            && formFiles && formFiles.length !== 0
            && formFiles[0].size >= Utility.infoObject.maxGlobalUploadSize)
        {
            const fileSizeMB = (Utility.infoObject.maxGlobalUploadSize / (1024 * 1024));
            const fileSizeString = fileSizeMB.toLocaleString(undefined, {
                maximumFractionDigits: 1
            });
            postErrorMessage = `Filesize must be less than ${fileSizeString} MB`;
            return;
        }

        if (formCaptcha == null) {
            postErrorMessage = "Invalid captcha";
            return;
        }

        if (isPosting) return;

        isPosting = true;

        try {
            postErrorMessage = "Uploading...";

            let uploadResponse;

            if (isThreadUploader) {
                uploadResponse = await UploadThread();
            } else {
                uploadResponse = await UploadPost();
            }

            if (uploadResponse !== undefined && !(uploadResponse.message === undefined || uploadResponse.message === null || uploadResponse.message === "")) {
                postErrorMessage = uploadResponse.message;

                dispatch("error", {
                    message: postErrorMessage,
                });
            } else {
                postErrorMessage = null;
                dispatch("success");
            }
        } catch (e) {
            console.log(e);
        }

        isPosting = false;
    }

    onMount(() => {
        const resetCaptcha = () => {
            formCaptcha = null;
        };

        (<any>window).hcaptcha.render("post-captcha", {
            // theme: "dark",
            callback: (response) => {
                formCaptcha = response;
            },
            "expired-callback": resetCaptcha,
            "close-callback": resetCaptcha,
            "error-callback": resetCaptcha,
        });
    });
</script>

<div class:hidden={showForm}>
    <button type="button" class="mx-auto d-block" on:click={() => showForm = true}>Create a thread</button>
</div>

<div class="reply-box border mb-5 container" class:hidden={!showForm}>
    {#if postErrorMessage !== null}
        <div class="row input-row">
            <div class="col-12 mt-1" style="color:red;">{postErrorMessage}</div>
        </div>
    {/if}
    {#if isThreadUploader}
        <div class="my-1" style="text-align: center">Create a new thread</div>
        <div class="row input-row">
            <div class="col-2">Subject</div>
            <div class="col-8">
                <input class="w-100" type="text" bind:value={formSubject} />
            </div>
        </div>
    {:else}
        <div class="my-1"></div>
    {/if}
    <div class="row input-row">
        <div class="col-2">Name</div>
        <div class="col-8">
            <input class="w-100" type="text" bind:value={formName} />
        </div>
        <div class="col-2 pl-0">
            <button
            class="w-100"
            on:click={Post}
            >Post</button>
        </div>
    </div>
    <div class="row input-row">
        <div class="col-2">Comment</div>
        <div class="col-10">
            <textarea class="w-100" bind:value={formText} />
        </div>
    </div>
    <div class="row input-row">
        <div class="col-2">File</div>
        <div class="col-10"><input type="file" bind:files={formFiles} /></div>
    </div>
    <div class="row input-row">
        <div class="col-2">Captcha</div>
        <div class="col-10">
            <div
                id="post-captcha"
                class="h-captcha"
                data-sitekey={Utility.infoObject.hCaptchaSiteKey}
            />
        </div>
    </div>
</div>

<style>
    .reply-box {
        max-width: 468px;
        /* background-color: #444; */
        background-color: var(--post-background-color);
        /* color: white; */
        color: var(--text-color);
        border-color: var(--post-border-color) !important;
    }

    .input-row {
        padding: 1px 0px;
    }

    button {
        border-radius: revert;
        font-family: var(--text-font);
    }

    .hidden {
        display: none;
    }
</style>
