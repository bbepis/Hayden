<script lang="ts">
    import { Utility } from "../../data/utility";

    let setBoardId: number;
    let setPostId: number;

    export const showModal = (boardId: number, postId: number) => {
        setBoardId = boardId;
        setPostId = postId;
        (<any>jQuery(deletePostModal)).modal();
    };

    let banImages: boolean = false;

    async function deletePost() {
        await Utility.PostForm("/moderator/deletepost", {
            boardId: setBoardId,
            postId: setPostId,
            banImages: banImages
        });

        (<any>jQuery(deletePostModal)).modal("hide");
    }

    let deletePostModal: HTMLDivElement;
</script>

<div
    bind:this={deletePostModal}
    class="modal fade"
    tabindex="-1"
    role="dialog"
    aria-labelledby="exampleModalLabel"
    aria-hidden="true"
>
    <div class="modal-dialog" role="document">
        <div class="modal-content">
            <div class="modal-header">
                <h5 class="modal-title" id="exampleModalLabel">Delete Post</h5>
                <button
                    type="button"
                    class="close"
                    data-dismiss="modal"
                    aria-label="Close"
                >
                    <span aria-hidden="true">&times;</span>
                </button>
            </div>
            <div class="modal-body">
                <div class="container">
                    <div class="row my-1">
                        <div class="col-4"></div>
                        <div class="col-8">
                            <div class="form-check">
                                <label class="form-check-label">
                                    <input class="form-check-input" type="checkbox" bind:checked={banImages}>Ban images
                                </label>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            <div class="modal-footer">
                <button
                    type="button"
                    class="btn btn-secondary"
                    data-dismiss="modal">Close</button
                >
                <button type="button" class="btn btn-primary" on:click={deletePost}
                    >Delete post</button
                >
            </div>
        </div>
    </div>
</div>

<style>
    .modal {
        color: #212529;
    }
</style>
