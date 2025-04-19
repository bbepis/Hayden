export interface InfoObject {
    apiEndpoint: string;
    rawEndpoint: string;
    hCaptchaSiteKey: string | null;
    maxGlobalUploadSize: number | null;
    siteName: string;
    quoteList: string[] | null;
    bannerFilename: string | null;
    newsItems: NewsItem[] | null;
    shiftJisArt: string | null;
    searchEnabled: boolean;
	compactBoardMode: boolean;
}

export interface NewsItem {
    Title: string;
    DateString: string;
    Content: string;
}

export interface BoardModel {
    id: number;

    shortName: string;
    longName: string;
    category: string;
    isNSFW: boolean;
    isReadOnly: boolean;

	threadCount?: number;
	postCount?: number;
	imageCount?: number;
}

export interface BoardPageModel {
    threads: ThreadModel[];

    totalThreadCount: number;
    boardInfo: BoardModel;
}

export interface ThreadModel {
    threadId: number;

    board: BoardModel;

    subject: string;

    lastModified: string;

    archived: boolean;
    deleted: boolean;

    posts: PostModel[];
}

export interface PostModel {
    postId: number;
    threadId: number;

    contentHtml: string | null;
    contentRaw: string | null;

    author: string | null;
    tripcode: string | null;

    dateTime: string;

    deleted: boolean;

    files: FileModel[];
}

export interface FileModel {
    fileId: number;

    md5Hash: Uint8Array;
    sha1Hash: Uint8Array;
    sha256Hash: Uint8Array;

    extension: string;

    imageWidth: number | null;
    imageHeight: number | null;

    fileSize: number;

    index: number;
    filename: string;

    spoiler: boolean;
    deleted: boolean;

    imageUrl: string;
    thumbnailUrl: string;
}

export interface ReportedPostModel {
	post: PostModel;
	board: BoardModel;

	reports: ReportModel[];
}

export interface ReportModel {
    id: number;
	ipAddress: string;
	reason: string;
	severity: number;
	resolved: boolean;
}

export enum ModeratorRole {
    Janitor = 1,
    Moderator = 2,
    Developer = 3,
    Admin = 4
}