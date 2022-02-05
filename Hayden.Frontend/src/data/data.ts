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