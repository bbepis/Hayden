<script lang="ts">
    import { Utility } from "../../data/utility";

    let setBoardId: number;
    let setPostId: number;

    export const showModal: (
        boardId: number,
        postId: number
    ) => void = (boardId: number, postId: number) => {
        setBoardId = boardId;
        setPostId = postId;
        (<any>jQuery(banUserModal)).modal();
    };

    let reasonPrivate: string = "";
    let reasonPublic: string = "";
    let hoursBan: number = 1;
    let permanent: boolean = false;

    async function sendBan() {
        await Utility.PostForm("/moderator/banuser", {
            boardId: setBoardId,
            postId: setPostId,
            seconds: hoursBan * 3600,
            indefinite: permanent,
            internalReason: reasonPrivate,
            publicReason: reasonPublic
        });

        (<any>jQuery(banUserModal)).modal("hide");
    }

    let banUserModal: HTMLDivElement;
</script>

<div
    bind:this={banUserModal}
    class="modal fade"
    tabindex="-1"
    role="dialog"
    aria-labelledby="exampleModalLabel"
    aria-hidden="true"
>
    <div class="modal-dialog" role="document">
        <div class="modal-content">
            <div class="modal-header">
                <h5 class="modal-title" id="exampleModalLabel">Ban User</h5>
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
                        <div class="col-4">Reason (private):</div>
                        <div class="col-8">
                            <input class="form-control" type="text" bind:value={reasonPrivate} />
                        </div>
                    </div>
                    <div class="row my-1">
                        <div class="col-4">Reason (public):</div>
                        <div class="col-8">
                            <input class="form-control" type="text" bind:value={reasonPublic} />
                        </div>
                    </div>
                    <div class="row my-1">
                        <div class="col-4">Hours ban:</div>
                        <div class="col-8">
                            <input class="form-control" type="number" bind:value={hoursBan} disabled={permanent} />
                        </div>
                    </div>
                    <div class="row my-1">
                        <div class="col-4"></div>
                        <div class="col-8">
                            <div class="form-check">
                                <label class="form-check-label">
                                    <input class="form-check-input" type="checkbox" bind:checked={permanent}>Permanent
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
                <button type="button" class="btn btn-primary" on:click={sendBan}
                    >Ban</button
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
