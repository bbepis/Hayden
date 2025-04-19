import type { PostModel } from "./data";

export function RenderRawPostYotsuba(post: PostModel): string {
    // convert post mentions into clickable links
	let newContent = post.contentRaw!.replace(/>>(\d+)/g, (match, capture1) => {
		let parsed = parseInt(capture1);
		return `<a class=\"quoteLink\" data-postid=\"${capture1}\" href=\"#p${capture1}\">&gt;&gt;${capture1}${post.threadId === parsed ? " (OP)" : ""}</a>`
	});

    // wrap quotes in quote tags
    newContent = newContent.replace(/^\ *(>[^>].+)/gm, "<span class=\"quote\">$1</span>");

    // convert spoiler tags
    newContent = newContent.replace(/\[spoiler\](.+?)\[\/spoiler\]/gm, "<s>$1</s>");

    // turn newlines into <br>
    newContent = newContent.replace(/\r?\n/g, "<br/>");

    // turn link text into clickable links
    newContent = newContent.replace(/(?:(?:https?|ftp|file):\/\/|www\.|ftp\.)(?:\([-A-Z0-9+&@#\/%=~_|$?!:,.]*\)|[-A-Z0-9+&@#\/%=~_|$?!:,.])*(?:\([-A-Z0-9+&@#\/%=~_|$?!:,.]*\)|[A-Z0-9+&@#\/%=~_|$])/igm, "<a href=\"$&\">$&</a>");

    return newContent;
}