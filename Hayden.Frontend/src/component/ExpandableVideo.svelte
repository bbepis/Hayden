<script lang="ts">

    let img : HTMLImageElement = $state();

    interface Props {
        thumbUrl: string;
        videoUrl: string;
        altText: string;
        expanded?: boolean;
        onClick?: () => void;
    }

    let {
        thumbUrl,
        videoUrl,
        altText,
        expanded = $bindable(false),
        onClick = () => {
        const newValue = !expanded;

        if (!newValue && !isElementInViewport(img)) {
            img.scrollIntoView();
        }

        expanded = newValue;
    }
    }: Props = $props();

    function isElementInViewport (el: Element) {
        const rect = el.getBoundingClientRect();

        return rect.y >= 0;
    }

    function onClickInternal(e: Event) {
        e.preventDefault();
        onClick();
    }

    function onClickClose(e: Event) {
        e.preventDefault();
        expanded = false;
    }
</script>

{#if expanded}
    <a onclick={onClickClose}>[Close]</a>
    <br/>
    <!-- svelte-ignore a11y_media_has_caption -->
    <video controls>
        <source src={videoUrl} />
    </video>
{:else}
    <a href={videoUrl} onclick={onClickInternal}>
        <img bind:this={img} src={thumbUrl} alt={altText} decoding="async"/>
    </a>
{/if}

<style>
    img {
        cursor: pointer;
        max-width: 100%;
    }
</style>