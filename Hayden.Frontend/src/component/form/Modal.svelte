<script lang="ts">
	interface Props {
		children?: import('svelte').Snippet;
        title?: string;
	}

	let { children, title = undefined }: Props = $props();

	let dialog: HTMLDialogElement | undefined = $state();

    export const show = () => {
        dialog?.showModal();
        bodyElement?.classList.add("overflow-y-hidden");
    }

    export const close = () => {
        dialog?.close();
        bodyElement?.classList.remove("overflow-y-hidden");
    }

    let bodyElement: HTMLElement | undefined = $state();

    function stopPropagation(e: Event) {
        e.stopImmediatePropagation();
    }

    function handleClose(e: Event) {
        e.stopPropagation();
        dialog?.close();
        bodyElement?.classList.remove("overflow-y-hidden");
    }
</script>

<svelte:body bind:this={bodyElement}/>

<!-- svelte-ignore a11y_click_events_have_key_events a11y_no_noninteractive_element_interactions -->
<dialog
    class="top-1/2 left-1/2 -translate-1/2 outline-none! postborder text bg-post-bg"
	bind:this={dialog}
    onclick={handleClose}
>
    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <div onclick={stopPropagation}>
        <div class="flex w-full bg-box-header py-1 px-3">
            <span class="mr-auto">{title}</span>
            <span class="cursor-pointer" onclick={close}>✕</span>
        </div>
        <div class="p-3">
            {@render children?.()}
        </div>
    </div>
</dialog>

<style>
	dialog::backdrop {
		background: rgba(0, 0, 0, 0.3);
	}
</style>
