using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using Moq;
using Moq.Protected;
using NUnit.Framework;

namespace Hayden.Tests.Api
{
	[TestFixture]
	public class TinyboardApiTests
	{
		private const string imageboardWebsite = "http://big.chungus/";

		private Mock<HttpMessageHandler> CreateMockClientHandler(HttpStatusCode code, string url, string response)
		{
			var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
			handlerMock
				.Protected()
				.Setup<Task<HttpResponseMessage>>(
					"SendAsync",
					ItExpr.Is<HttpRequestMessage>(x => url == null || x.RequestUri.AbsoluteUri == url),
					ItExpr.IsAny<CancellationToken>()
				)
				.ReturnsAsync(new HttpResponseMessage
				{
					StatusCode = code,
					Content = response == null ? null : new StringContent(response, Encoding.UTF8, "application/json")
				})
				.Verifiable();

			return handlerMock;
		}

		private TinyboardApi tinyboardApi = new(new SourceConfig { ImageboardWebsite = imageboardWebsite });

		private HttpClient CreateMockClient(HttpStatusCode code, string url, string response)
			=> new HttpClient(CreateMockClientHandler(code, url, response).Object);
		
		[Test]
		public async Task GetThread_ParsesResponseCorrectly()
		{
			var testResponse = Utility.GetEmbeddedText("Hayden.Tests.TestData.tinyboard-crystalcafe.html");

			var mockClient = CreateMockClient(HttpStatusCode.OK, $"{imageboardWebsite}media/res/1234.html", testResponse);

			var result = await tinyboardApi.GetThread("media", 1234, mockClient);

			Assert.AreEqual(ResponseType.Ok, result.ResponseType);
			Assert.IsNotNull(result.Data);

			Assert.AreEqual(8, result.Data.Posts.Length);

			var opPost = result.Data.Posts[0];

			Assert.AreEqual(7202UL, opPost.PostNumber);
			Assert.AreEqual(ContentType.Tinyboard, opPost.ContentType);
			Assert.IsTrue(opPost.ContentRendered.Contains("Is it possible to make crystal cafe work with 4chan X?"));

			// put more here. i'm lazy
		}

		[Test]
		public async Task GetBoard_ParsesResponseCorrectly()
		{
			var testResponse = Utility.GetEmbeddedText("Hayden.Tests.TestData.tinyboard-crystalcafe-catalog.html");

			var mockClient = CreateMockClient(HttpStatusCode.OK, $"{imageboardWebsite}media/catalog", testResponse);
			
			var result = await tinyboardApi.GetBoard("media", mockClient);

			Assert.AreEqual(ResponseType.Ok, result.ResponseType);
			Assert.IsNotNull(result.Data);

			Assert.AreEqual(245, result.Data.Length);

			var firstThread = result.Data[0];
			
			Assert.AreEqual(30470UL, firstThread.ThreadNumber);
			//Assert.AreEqual(1576181967UL, firstThread.LastModified);
			Assert.AreEqual("Lisa The Painful RPG", firstThread.Subject);
		}
	}
}