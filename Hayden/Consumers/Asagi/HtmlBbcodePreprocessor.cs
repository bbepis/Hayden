using System.Linq;
using System.Text;
using AngleSharp.Html.Parser;
using AngleSharp.Text;
using Nest;

namespace Hayden.Consumers.Asagi
{
	public static class HtmlBbcodePreprocessor
	{
		private static readonly string[] voidTags = { "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr" };

		public static string PreprocessAsagiHtml(string html)
		{
			var textSource = new TextSource(html);

			var builder = new StringBuilder();

			var tokens = textSource.Tokenize().ToArray();

			int stackLayer = 0;
			int i = 0;

			void skipUntilClosing(string tagName)
			{
				int closingStackLayer = stackLayer - 1;

				for (; i < tokens.Length; i++)
				{
					var token = tokens[i];

					if (token.Type == HtmlTokenType.StartTag)
					{
						var tag = token.AsTag();

						if (!tag.IsSelfClosing && !voidTags.Contains(tag.Name))
							stackLayer++;
					}
					else if (token.Type == HtmlTokenType.EndTag)
					{
						if (!voidTags.Contains(token.Name))
							stackLayer--;

						if (closingStackLayer == stackLayer)
							break;
					}
				}
			}

			for (; i < tokens.Length; i++)
			{
				var token = tokens[i];

				if (token.Type == HtmlTokenType.StartTag)
				{
					var tag = token.AsTag();

					if (!tag.IsSelfClosing && !voidTags.Contains(tag.Name))
						stackLayer++;

					if (tag.Name == "span" 
					    && tag.Attributes.Any(x => x.Name == "class" && x.Value == "capcodeReplies"))
					{
						// quoteLinkRegex

						i++;
						skipUntilClosing("span");
					}
					else if ((tag.Name == "b" || tag.Name == "strong")
					    && tag.Attributes.Any(x => x.Name == "style" && (x.Value == "color: red;" || x.Value == "color:red;")))
					{
						// bannedRegex

						builder.AppendFormat($"[banned]{tokens[++i].Data}[/banned]");
						i++;
					}
					else if (tag.Name == "b" || tag.Name == "strong")
					{
						// boldRegex

						builder.AppendFormat($"[b]{tokens[++i].Data}[/b]");
						i++;
					}
					else if (tag.Name == "pre")
					{
						// codeTagRegex

						builder.AppendFormat($"[code]{tokens[++i].Data}[/code]");
						i++;
					}
					else if (tag.Name == "span"
					    && tag.Attributes.Any(x => x.Name == "class" && x.Value == "fortune"))
					{
						// fortuneRegex

						string color = tag.GetAttribute("style").Substring(6).Trim();

						i += 4;

						builder.AppendFormat($"\n\n[fortune color=\"{color}\"]{tokens[i].Data}[/fortune]");

						i++;
					}
					else if (tag.Name == "table"
					    && tag.Attributes.Any(x => x.Name == "class" && x.Value == "exif"))
					{
						// exifCleanRegex

						i++;
						skipUntilClosing("table");
					}
					else if (tag.Name == "span"
					         && tag.Attributes.Any(x => x.Name == "class" && x.Value == "math"))
					{
						// mathTagRegex

						builder.AppendFormat($"[math]{tokens[++i].Data}[/math]");
						i++;
					}
					else if (tag.Name == "div"
					         && tag.Attributes.Any(x => x.Name == "class" && x.Value == "math"))
					{
						// mathTagRegex

						builder.AppendFormat($"[eqn]{tokens[++i].Data}[/eqn]");
						i++;
					}
				}
				else if (token.Type == HtmlTokenType.EndTag)
				{
					stackLayer--;
				}
				else if (token.Type == HtmlTokenType.Character)
				{
					builder.Append(token.Data);
				}
			}

			//HtmlTokenizer.Tokenize(html);



			return builder.ToString().Trim();
		}
	}
}
