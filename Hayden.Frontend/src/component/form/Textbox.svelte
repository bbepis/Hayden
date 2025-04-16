<script lang="ts">
	import type { HTMLInputAttributes } from "svelte/elements";
	import { Search } from "@lucide/svelte";

	type Props = HTMLInputAttributes & {
		showSearch?: boolean;
		class?: string;
		value?: string;
		area?: boolean;
	};

	let focused = $state(false);

	let {
		showSearch = false,
		class: className = "",
		value = $bindable(),
		area = false,
		...restProps
	}: Props = $props();
</script>

<div class="relative">
	{#if showSearch}
		<div class="absolute left-[5px] top-1/2 mt-[-9px] opacity-90 pointer-events-none"><Search size={18} color={focused ? "var(--color-highlight)" : "currentColor"} /></div>
	{/if}
	{#if area}
		<textarea class="w-full h-full textbox-container focus:outline-none focus:border-highlight! rounded px-1 py-2 {className}"
			bind:value={value}
			onblur={() => focused = false}
			{...restProps}
			onfocus={e => {
				focused = true;
				restProps.onfocus?.(e);
			}}
		></textarea>
	{:else}
		<input type="text" class="w-full h-full textbox-container focus:outline-none focus:border-highlight! rounded px-1 py-1 {showSearch ? "pl-[28px]!" : ""} {className}"
			bind:value={value}
			onblur={() => focused = false}
			{...restProps}
			onfocus={e => {
				focused = true;
				restProps.onfocus?.(e);
			}}
		/>
	{/if}
	
</div>