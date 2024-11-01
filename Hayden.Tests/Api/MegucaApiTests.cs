using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Config;
using Moq;
using Moq.Protected;
using NUnit.Framework;

using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Hayden.Tests.Api;

[TestFixture]
public class MegucaApiTests
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

	private MegucaApi megucaApi = new(new SourceConfig { ImageboardWebsite = imageboardWebsite });

	private HttpClient CreateMockClient(HttpStatusCode code, string url, string response)
		=> new HttpClient(CreateMockClientHandler(code, url, response).Object);
		
	[TestCase("2chen", 11, TestName = "GetCapabilties - 2chen")]
	[TestCase("meguchan", 10, TestName = "GetCapabilties - Meguchan")]
	public async Task GetCapabilities_Works(string htmlName, int boardCount)
	{
		var testResponse = Utility.GetEmbeddedText($"Hayden.Tests.TestData.{htmlName}.html");

		var mockClient = CreateMockClient(HttpStatusCode.OK, imageboardWebsite, testResponse);

		var capabilities = await megucaApi.DetermineCapabilitiesAsync(mockClient);

		Assert.IsNotNull(capabilities);
		Assert.IsTrue(capabilities.SupportsBoardListing);
		Assert.IsNotNull(capabilities.BoardList);
		Assert.AreEqual(boardCount, capabilities.BoardList.Length);

		foreach (var board in capabilities.BoardList)
			Console.WriteLine($"/{board}/");
	}
}