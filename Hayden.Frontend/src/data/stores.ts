import { writable } from "svelte/store"
import { Api } from "./api";
import type { BoardModel, ModeratorRole } from "./data";

export class Exception {
    exceptionObject: any

    constructor(exception: any) {
        this.exceptionObject = exception;
    }
}

export const moderatorUserStore = writable<ModeratorRole | null>(null);
export const boardInfoStore = writable<Promise<BoardModel[]> | null>(null);

export const theme = writable(localStorage.getItem("hayden_theme") || 'yotsuba')
theme.subscribe((value) => localStorage.setItem("hayden_theme", value))

export async function initStores() {
    Api.GetUserInfoAsync()
        .then(value => moderatorUserStore.set(value.role))
        .catch(reason => moderatorUserStore.set(null));

    boardInfoStore.set(Api.GetBoardInfoAsync());
    // Api.GetBoardInfoAsync()
    //     .then(availableBoardsStore.set)
    //     .catch(reason => moderatorUserStore.set(null));
}