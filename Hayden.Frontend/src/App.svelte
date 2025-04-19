<script lang="ts">
	import Layout from "./Layout.svelte"
    import { theme } from "./data/stores";

	import IndexPage from "./page/IndexPage.svelte";
	import ThreadPage from "./page/ThreadPage.svelte";
	import SearchPage from "./page/SearchPage.svelte";
	import BoardPage from "./page/BoardPage.svelte";
	import AdminPage from "./page/admin/AdminPage.svelte";
	import LoginPage from "./page/admin/LoginPage.svelte";
	import RegisterPage from "./page/admin/RegisterPage.svelte";
	import ReportModal from "./component/modal/ReportModal.svelte";
	import DeletePostModal from "./component/modal/DeletePostModal.svelte";
	import BanUserModal from "./component/modal/BanUserModal.svelte";
	import Router from "svelte-spa-router"
	import { setContext } from "svelte";
	import HoverPost from "./component/HoverPost.svelte";
	import LoadPostPage from "./page/LoadPostPage.svelte";

	theme.subscribe(currentTheme => {
		document.documentElement.className = `theme-${currentTheme}`;
	});

	let banUserModal: BanUserModal | undefined = $state();
	let deletePostModal: DeletePostModal | undefined = $state();
	let reportModal: ReportModal | undefined = $state();

	setContext("reportPost", (boardId: number, postId: number) => reportModal?.showModal(boardId, postId));
	setContext("deletePost", (boardId: number, postId: number) => deletePostModal?.showModal(boardId, postId));
	setContext("banUser", (boardId: number, postId: number) => banUserModal?.showModal(boardId, postId));

	const routes = {
		"/": IndexPage,
		"/search": SearchPage,
		"/admin/login": LoginPage,
		"/admin/register": RegisterPage,
		"/admin/": AdminPage,
		"/:board": BoardPage,
		"/:board/page/:page": BoardPage,
		"/:board/thread/:threadid": ThreadPage,
		"/:board/post/:postid": LoadPostPage,
	};
</script>

<Layout>
	<Router {routes}/>
</Layout>

<BanUserModal bind:this={banUserModal} />
<DeletePostModal bind:this={deletePostModal} />
<ReportModal bind:this={reportModal} />

<HoverPost />

<style>

</style>