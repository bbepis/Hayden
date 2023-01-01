<script lang="ts">
    import type { BoardModel, PostModel } from "../data/data";
    import moment from "moment";
    import ExpandableImage from "./ExpandableImage.svelte";
    import { onMount } from "svelte";
    import { Utility } from "../data/utility";
    import { RenderRawPost } from "../data/postrender";
    import PostMenu from "./PostMenu.svelte";
    import { moderatorUserStore } from "../data/stores";
    import ExpandableVideo from "./ExpandableVideo.svelte";

    export let threadId: number;
    export let post: PostModel;
    export let board: BoardModel;
    export let subject: string = null;
    export let backquotes: number[] = null;

    function getDateTime() {
        if (post.dateTime.endsWith("Z")) {
            return post.dateTime;
        }
        
        return post.dateTime + "Z";
    }

    const time = moment(getDateTime());

    let showDropdown: boolean = false;
    let menu: HTMLElement;

    onMount(() => {
        jQuery(".post-contents a").attr("tinro-ignore", "true");
    });

    function toggleMenu(value: boolean | null) {
        showDropdown = value ?? !showDropdown;

        if (showDropdown) {
            //setImmediate(() => {menu.focus();});
            setTimeout(() => {menu.focus();}, 0);
        }
    }

    function menuKeyDown(e: KeyboardEvent) {
        if (e.keyCode === 27) {
            menu.blur();
            e.preventDefault();
        }
    }
</script>

<div id="p{post.postId}" class="post reply">
    <div id="pi{post.postId}" class="postInfo">
        {#if subject}
            <span class="subject">{subject}</span>
        {/if}
        <span class="nameBlock">
            <span class="name">{post.author ?? "Anonymous"}</span>
        </span>
        <span title={time.fromNow()}>
            {time.local().format("ddd DD/MM/yy h:mm:ss A")}
        </span>
        <span>
            <a href="/{board.shortName}/thread/{threadId}#p{post.postId}"
                >No. {post.postId}</a
            >
        </span>

        {#if $moderatorUserStore}
            <span class="menu-button" on:click={() => toggleMenu(!showDropdown)}>
                ▼
                <div tabindex="-1"
                    class="menu" class:hidden={!showDropdown}
                    on:blur={() => {toggleMenu(false);}}
                    on:keydown={menuKeyDown}
                    on:click={e => {e.stopPropagation();}}
                    bind:this={menu}>
                    <PostMenu on:postaction boardId={board.id} postId={post.postId} />
                </div>
            </span>
        {/if}
        {#if backquotes}
            {#each backquotes as backquoteId (backquoteId)}
                <div class="backquote">
                    <a href="#p{backquoteId}" class="quotelink" tinro-ignore
                        >&gt;&gt;{backquoteId}</a
                    >
                </div>
            {/each}
        {/if}
    </div>
    {#if post.files.length === 1}
        {@const file = post.files[0]}
        <div class="file">
            <div class="fileText">
                <a href={file.imageUrl} tinro-ignore
                    >{file.filename}.{file.extension}</a
                >
                ({Utility.ToHumanReadableSize(file.fileSize)}{post
                    .files[0].imageWidth !== null
                    ? `, ${file.imageWidth} x ${file.imageHeight}`
                    : ""})
            </div>
            <a class="fileThumb" href={file.imageUrl} tinro-ignore>
                <!-- <img src={post.thumbnailUrl} alt={post.post.mediaFilename}/> -->
                {#if file.extension === "webm"}
                    <ExpandableVideo
                        videoUrl={file.imageUrl}
                        thumbUrl={file.thumbnailUrl}
                        altText={file.filename}
                    />
                {:else}
                    <ExpandableImage
                        fullImageUrl={file.imageUrl}
                        thumbUrl={file.thumbnailUrl}
                        altText={file.filename}
                    />
                {/if}
            </a>
        </div>
    {:else if post.files.length > 1}
        <div class="panelUploads multipleUploads">
            {#each post.files as file}
                    <figure class="uploadCell">
                        <div class="uploadDetails">
                            <span class="hideMobile">(</span><span class="sizeLabel"
                                >{Utility.ToHumanReadableSize(file.fileSize)}</span
                            > <span class="dimensionLabel">{file.imageWidth}x{file.imageHeight}</span>
                            <a
                                class="originalNameLink"
                                href={file.imageUrl}
                                download="{file.filename}.{file.extension}">{file.filename}.{file.extension}</a
                            ><span class="hideMobile">)</span>
                        </div>

                        <div />
                        {#if file.extension === "webm"}
                            <ExpandableVideo
                                videoUrl={file.imageUrl}
                                thumbUrl={file.thumbnailUrl}
                                altText={file.filename}
                            />
                        {:else}
                            <ExpandableImage
                                fullImageUrl={file.imageUrl}
                                thumbUrl={file.thumbnailUrl}
                                altText={file.filename}
                            />
                        {/if}
                    </figure>
            {/each}
        </div>
    {/if}
    <blockquote class="post-contents">
        {#if post.contentRaw}
            {@html RenderRawPost(post.contentRaw)}
        {:else if post.contentHtml}
            {@html post.contentHtml.replace("\n", "<br/>")}
        {/if}
    </blockquote>
</div>

<style>
    .backquote {
        display: inline-block;
        padding-left: 3px;
        padding-right: 3px;
    }

    .hidden {
        display: none;
    }

    .menu-button {
        cursor: pointer;
        position: relative;
    }

    .menu {
        position: absolute;
        top: 100%;
        left: 0;
        cursor: initial;
    }

    .post {
        overflow: initial;
    }
</style>
