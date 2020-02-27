using System.Collections;
using Hayden.Consumers;
using NUnit.Framework;

namespace Hayden.Tests.Consumers
{
	[TestFixture]
	public class AsagiThreadConsumerTests
	{
		public static IEnumerable CleanCommentTestCases()
		{
			const string loremIpsum = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Vivamus blandit mauris ac purus egestas, eu interdum nibh dignissim.";

			yield return Utility.CreateTestCaseData("Comment with no formatting should not be cleaned", loremIpsum, loremIpsum);

			foreach (string roleName in new[] { "Administrator", "Moderator", "Developer" })
				foreach (string replyText in new[] { "Reply", "Replies" })
				{
					yield return Utility.CreateTestCaseData($"Admin quote link cleaning test - {roleName} - {replyText}",
						$"<span class=\"capcodeReplies\"><span style=\"font-size: smaller;\"><span style=\"font-weight: bold;\">{roleName} {replyText}:</span>{loremIpsum}</span><br></span>",
						"");
				}


			foreach (string tagName in new[] { "banned", "moot", "spoiler", "code" })
			{
				yield return Utility.CreateTestCaseData($"Non-public tags are preserved correctly - {tagName}",
					$"[{tagName}]{loremIpsum}[/{tagName}]",
					$"[{tagName}:lit]{loremIpsum}[/{tagName}:lit]");
			}

			yield return Utility.CreateTestCaseData("Toggle exif expansion span removed",
				$"<span class=\"abbr\">{loremIpsum}</span>",
				"");

			yield return Utility.CreateTestCaseData("Exif table removed",
				$"<table class=\"exif\"[^>]*>{loremIpsum}</table>",
				"");

			yield return Utility.CreateTestCaseData("Oekaki draw section removed",
				$"<br><br><small><b>Oekaki Post</b>{loremIpsum}</small>",
				"");

			foreach (var tagName in new[] { "strong", "b" })
			{
				yield return Utility.CreateTestCaseData($"Banned HTML cleaned to BBCode - {tagName}",
					$"<{tagName} style=\"color: red;\">{loremIpsum}</{tagName}>",
					$"[banned]{loremIpsum}[/banned]");
			}

			yield return Utility.CreateTestCaseData("Moot comment cleaned to BBCode",
				$"<div style=\"padding: 5px;margin-left: .5em;border-color: #faa;border: 2px dashed rgba(255,0,0,.1);border-radius: 2px\">{loremIpsum}</div>",
				$"[moot]{loremIpsum}[/moot]");

			foreach (var colorName in new[] { "red", "blue", "green", "purple" })
			{
				yield return Utility.CreateTestCaseData($"Fortune converted to BBCode - {colorName}",
					$"<span class=\"fortune\" style=\"color:{colorName}\"><br><br><b>{loremIpsum}</b></span>",
					$"[fortune color=\"{colorName}\"]{loremIpsum}[/fortune]");
			}

			foreach (var tagName in new[] { "strong", "b" })
			{
				yield return Utility.CreateTestCaseData($"Bold HTML cleaned to BBCode - {tagName}",
					$"<{tagName}>{loremIpsum}</{tagName}>",
					$"[b]{loremIpsum}[/b]");
			}
			
			yield return Utility.CreateTestCaseData("Code pre-formatted HTML cleaned to BBCode",
				$"<pre>{loremIpsum}</pre>",
				$"[code]{loremIpsum}[/code]");
			
			yield return Utility.CreateTestCaseData("Math HTML cleaned to BBCode - general math",
				$"<span class=\"math\">{loremIpsum}</span>",
				$"[math]{loremIpsum}[/math]");
			
			yield return Utility.CreateTestCaseData("Math HTML cleaned to BBCode - equation",
				$"<div class=\"math\">{loremIpsum}</div>",
				$"[eqn]{loremIpsum}[/eqn]");
			
			yield return Utility.CreateTestCaseData("Quote HTML cleaned - Format 1",
				$"<font class=\"unkfunc\">{loremIpsum}</font>",
				loremIpsum);
			
			yield return Utility.CreateTestCaseData("Quote HTML cleaned - Format 2",
				$"<span class=\"quote\">{loremIpsum}</span>",
				loremIpsum);
			
			yield return Utility.CreateTestCaseData("Quote HTML cleaned - Format 3",
				$"<span class=\"deadlink\">{loremIpsum}</span>",
				loremIpsum);
			
			yield return Utility.CreateTestCaseData("Link HTML cleaned",
				$"<a>{loremIpsum}</a>",
				loremIpsum);
			
			yield return Utility.CreateTestCaseData("Spoiler HTML cleaned to BBCode - Old format",
				$"<span class=\"spoiler\">{loremIpsum}</span>",
				$"[spoiler]{loremIpsum}[/spoiler]");
			
			yield return Utility.CreateTestCaseData("Spoiler HTML cleaned to BBCode - New format",
				$"<s>{loremIpsum}</s>",
				$"[spoiler]{loremIpsum}[/spoiler]");
			
			yield return Utility.CreateTestCaseData("ShiftJIS Japanese HTML cleaned to BBCode",
				$"<span class=\"sjis\">{loremIpsum}</span>",
				$"[shiftjis]{loremIpsum}[/shiftjis]");
			
			yield return Utility.CreateTestCaseData("Newline tags converted to newline characters - With slash",
				$"{loremIpsum}<br/>{loremIpsum}",
				$"{loremIpsum}\n{loremIpsum}");
			
			yield return Utility.CreateTestCaseData("Newline tags converted to newline characters - Without slash",
				$"{loremIpsum}<br>{loremIpsum}",
				$"{loremIpsum}\n{loremIpsum}");
			
			yield return Utility.CreateTestCaseData("Word break tags removed",
				$"{loremIpsum}<wbr>{loremIpsum}",
				$"{loremIpsum}{loremIpsum}");
			
			yield return Utility.CreateTestCaseData("Input is trimmed",
				$" \r\n{loremIpsum}\r\n ",
				loremIpsum);
		}
			

		[TestCaseSource(nameof(CleanCommentTestCases))]
		public void CleanCommentTest(string input, string expected)
		{
			var cleanedComment = AsagiThreadConsumer.CleanComment(input);

			Assert.AreEqual(expected, cleanedComment, "data was not expected");
		}
	}
}