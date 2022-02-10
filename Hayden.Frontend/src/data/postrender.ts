export function RenderRawPost(rawPost: string): string {
    let newContent = rawPost.replace(/(>>(\d+))/g, "<a class=\"quoteLink\" href=\"#p$2\" tinro-ignore=\"true\">$1</a>");
    
    newContent = newContent.replace(/^\ *(>[^>].+)/gm, "<span class=\"quote\">$1</span>");

    newContent = newContent.replace(/\[spoiler\](.+?)\[\/spoiler\]/gm, "<s>$1</s>");

    newContent = newContent.replace(/\r?\n/g, "<br/>");

    return newContent;
}