import type { InfoObject } from "./data";

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

	static ToHumanReadableSize(size: number): string {
		if (size === 0 || size === null || typeof (size) === "undefined")
			return "0 B";

		let i: number = Math.floor(Math.log(size) / Math.log(1024));

		if (i === 0)
			return `${size} B`

		return `${(size / Math.pow(1024, i)).toFixed(2)} ${['B', 'KB', 'MB', 'GB', 'TB'][i]}`;
	};

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

		let url = this.infoObject.apiEndpoint + endpoint;

		if (Array.from(searchParams.entries()).length > 0) {
			url += "?" + searchParams.toString();
		}

		const result = await fetch(url);

		if (!result.ok) {
			throw result;
		}

		return await result.json();
	}

	static async PostForm(endpoint: string, data: any): Promise<Response> {

		let formData = new FormData();

		for (var key of Object.keys(data)) {
			const value = data[key];

			if (value === null || value === undefined)
				continue;

			formData.set(key, data[key]);
		}

		return await Utility.Post(endpoint, formData);
	}

	static async Post(endpoint: string, body?: any): Promise<Response> {

		let url = this.infoObject.apiEndpoint + endpoint;

		const result = await fetch(url, {
			method: "post",
			body: body,
			credentials: "include"
		});

		// if (!result.ok) {
		// 	throw result;
		// }

		return result;
	}

	static ToInstance<T>(obj: T, json: string): T {
		var jsonObj = JSON.parse(json);

		if (typeof obj["fromJSON"] === "function") {
			obj["fromJSON"](jsonObj);
		}
		else {
			for (var propName in jsonObj) {
				obj[propName] = jsonObj[propName]
			}
		}

		return obj;
	}

	static TryCastInt(value: string): number | null {
		const number = parseInt(value);

		if (isNaN(number))
			return null;

		return number;
	}

	static RangeTo(startIndex: number, endIndex: number): number[] {
		let outputArray: number[] = []

		let counter = 0;
		while (counter + startIndex < endIndex) {
			outputArray.push(startIndex + counter);
			counter++;
		}

		return outputArray;
	}

	static IsNotEmpty(value: string | null | undefined) {
		if (value === null || value === undefined || value.length === 0)
			return false;

		return true;
	}

	static groupByArray<TItem, TKey>(xs : TItem[], key : string | ((x: TItem) => TKey)) : { key: TKey, values : TItem[] }[] {
		return xs.reduce(function (rv, x) {
			let v = key instanceof Function ? key(x) : x[key];
			let el = rv.find((r) => r && r.key === v);
			if (el) {
				el.values.push(x);
			}
			else {
				rv.push({ key: v, values: [x] });
			}
			return rv;
		}, []);
	}
}