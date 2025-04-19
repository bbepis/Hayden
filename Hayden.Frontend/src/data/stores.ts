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
export const searchParamStore = writable<Record<string, string> | null>(null);

export const postHoverStore = writable<{ boardId: number, postId: number } | undefined>();

export const statusStore = writable("Idle");
export const progressStore = writable(0);

export const theme = writable(localStorage.getItem("hayden_theme") || 'eclipse')
theme.subscribe((value) => localStorage.setItem("hayden_theme", value))

export async function initStores() {
    Api.GetUserInfoAsync()
        .then(value => moderatorUserStore.set(value.role))
        .catch(reason => moderatorUserStore.set(null));

    boardInfoStore.set(Api.GetBoardInfoAsync());

    // boardInfoStore.set((async () => ([
	// 	{
	// 		id: 0,
	// 		category: "4chan",
	// 		longName: "Random",
	// 		shortName: "b",
	// 		isNSFW: true,
	// 		isReadOnly: true
	// 	},
	// 	{
	// 		id: 1,
	// 		category: "4chan",
	// 		longName: "Vtubers",
	// 		shortName: "vt",
	// 		isNSFW: false,
	// 		isReadOnly: true
	// 	},
	// ]))());


    // Api.GetBoardInfoAsync()
    //     .then(availableBoardsStore.set)
    //     .catch(reason => moderatorUserStore.set(null));
}