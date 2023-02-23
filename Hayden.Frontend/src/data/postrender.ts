export function RenderRawPost(rawPost: string): string {
    // convert post mentions into clickable links
    let newContent = rawPost.replace(/(>>(\d+))/g, "<a class=\"quoteLink\" href=\"#p$2\" tinro-ignore=\"true\">$1</a>");
    
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