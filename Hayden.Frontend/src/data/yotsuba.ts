export class YotsubaThread {
    public posts: YotsubaPost[]

    public extension_isdeleted: boolean | null;

    public get op() : YotsubaPost | null {
        if (this.posts.length > 0) {
            return this.posts[0];
        }

        return null;
    }
}

export class YotsubaPost {
    /** Post number */
    public no: number

    public name: string

    /** Thread subject */
    public sub: string

    /** Post HTML */
    public com: string

    /** Unix timestamp */
    public time: number

    public extension_isdeleted: boolean | null;
}