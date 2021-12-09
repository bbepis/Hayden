export class InfoObject {
    endpoint: string;
}

export class Thread {
    board: string;
    threadId: number;

    title: string;

    lastModified: string;

    isArchived: boolean;
    isDeleted: boolean;
}

export class PostModel {
    post: Post;
    hasFile: boolean;
    imageUrl: string;
    thumbnailUrl: string;
}

export class Post {
    board: string;
    postId: number;
    threadId: number;

    html: string | null;

    author: string;
    mediaHash: Uint8Array;

    mediaFilename: string;

    dateTime: string;

    isSpoiler: boolean;
    isDeleted: boolean;
    isImageDeleted: boolean;
}

export class ThreadModel {
    thread: Thread;
    posts: PostModel[];
}

export class Utility {
    static byteToHex: string[] = [];
    static infoObject: InfoObject;

    private static _staticConstructor = (function () {
        for (let n = 0; n <= 0xff; ++n) {
            const hexOctet = n.toString(16).padStart(2, "0");
            Utility.byteToHex.push(hexOctet);
        }
    })();

    static ToHex(bytes: Uint8Array) {
        const hexOctets = []; // new Array(buff.length) is even faster (preallocates necessary array size), then use hexOctets[i] instead of .push()

        for (let i = 0; i < bytes.length; ++i)
            hexOctets.push(Utility.byteToHex[bytes[i]]);

        return "0x" + hexOctets.join("");
    }

    static DivRem(bigInt: bigint, divisor: bigint): [bigint, bigint] {
        const remainder = bigInt % divisor;
        return [bigInt / divisor, remainder];
    }

    static ToBase36(arr: Uint8Array): string {
        const charSet = "0123456789abcdefghijklmnopqrstuvwxyz";
        const divisor = BigInt(36);
        let bigInt = BigInt(Utility.ToHex(arr));
        let remainder: bigint;

        let result = "";

        while (bigInt > 0) {
            [bigInt, remainder] = this.DivRem(bigInt, divisor);

            result += charSet[Number(remainder)];
        }

        return result;
    }

    static ToLocalTime(dateString: string): Date {
        return new Date(dateString + "Z");
    }

    static async FetchData(endpoint: string, data: any = null): Promise<any> {
        const searchParams = new URLSearchParams();
        
        if (data) {
            for (var key of Object.keys(data)) {
                searchParams.set(key, data[key]);
            }
        }

        let url = this.infoObject.endpoint + endpoint;

        if (Array.from(searchParams.entries()).length > 0) {
            url += "?" + searchParams.toString();
        }

		const result = await fetch(url);
		
		if (!result.ok) {
			throw result;
		}

		return await result.json();
	}
}