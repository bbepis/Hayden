<script lang="ts">
    import { onMount } from "svelte";

    export let thumbUrl: string;
    export let fullImageUrl: string;
    export let altText: string;
    export let expanded: boolean = false;

    let img : HTMLImageElement;

    let loading: boolean = false;

    export let onClick: () => void = () => {
        const newValue = !expanded;
        
        if (!newValue && !isElementInViewport(img)) {
            img.scrollIntoView();
        }
        else if (newValue) {
            loading = true;
        }

        expanded = newValue;
    };

    function isElementInViewport (el: Element) {
        const rect = el.getBoundingClientRect();

        return rect.y >= 0;
    }

    function onClickInternal(e: Event) {
        e.preventDefault();
        onClick();
    }

    onMount(() => {
        img.addEventListener("load", () => {
            loading = false;
        });
    });

    let currentUrl: string;

    $: currentUrl = expanded ? fullImageUrl : thumbUrl;
</script>

<a href={fullImageUrl} on:click={onClickInternal}>
    <img bind:this={img} src={currentUrl} alt={altText} class:loading={loading} decoding="async"/>
</a>

<style>
    img {
        cursor: pointer;
        max-width: 100%;
    }

    .loading {
        opacity: 50%;
    }
</style>