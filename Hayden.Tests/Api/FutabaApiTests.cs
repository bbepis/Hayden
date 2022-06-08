using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Hayden.Tests.Api
{
	[TestFixture]
	public class FutabaApiTests
	{
		private FutabaApi futabaApi = new FutabaApi("https://may.2chan.net/");

		[Test]
		public async Task Test2chanThreadParser()
		{
			var mockClient = TestCommon.CreateResourceMockClientHandler(HttpStatusCode.OK,
				"Hayden.Tests.Api.TestData.futaba-thread-2chan.html");

			using var client = new HttpClient(mockClient.Object, false);

			//var board = await futabaApi.GetBoard("comfy", client);
			var thread = await futabaApi.GetThread("b", 977812281, client);
		}
	}
}