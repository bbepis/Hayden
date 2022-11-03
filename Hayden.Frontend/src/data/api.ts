import type { BoardPageModel } from "./data";
import { Utility } from "./utility";

export class Api {
    static async GetBoardPage(board: string, page: number | null = null): Promise<BoardPageModel>
    {
        const params = {}
        if (page !== null) {
            params["page"] = page.toString();
        }

        return <Promise<BoardPageModel>>Utility.FetchData(`/board/${board}/index`, params);
    }

    static async UserLoginAsync(username: string, password: string): Promise<boolean>
    {
        const result = await Utility.PostForm("/user/login", {
            username: username,
            password: password
        });

        return result.ok;
    }

    static async UserRegisterAsync(username: string, password: string, registerCode: string): Promise<boolean>
    {
        const result = await Utility.PostForm("/user/register", {
            username: username,
            password: password,
            registerCode: registerCode
        });

        return result.ok;
    }
}