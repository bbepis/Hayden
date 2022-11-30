import { writable } from "svelte/store"
import { Api } from "./api";
import type { ModeratorRole } from "./data";

export const moderatorUserStore = writable<ModeratorRole | null>(null);

export const theme = writable(localStorage.getItem("hayden_theme") || 'tomorrow')
theme.subscribe((value) => localStorage.setItem("hayden_theme", value))

export async function initStores() {
    const userInfo = await Api.GetUserInfoAsync();

    moderatorUserStore.set(userInfo.role);
}