using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;

namespace Hayden.Tests
{
	internal static class TestCommon
	{
		internal static Mock<HttpMessageHandler> CreateMockClientHandler(HttpStatusCode code, HttpContent content)
		{
			var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
			handlerMock
				.Protected()
				.Setup<Task<HttpResponseMessage>>(
					"SendAsync",
					ItExpr.IsAny<HttpRequestMessage>(),
					ItExpr.IsAny<CancellationToken>()
				)
				.ReturnsAsync(new HttpResponseMessage
				{
					StatusCode = code,
					Content = content
				})
				.Verifiable();

			return handlerMock;
		}

		internal static Mock<HttpMessageHandler> CreateMockClientHandler(HttpStatusCode code, string response, string mediaType)
			=> CreateMockClientHandler(code, response == null ? null : new StringContent(response, Encoding.UTF8, mediaType));

		internal static Mock<HttpMessageHandler> CreateJsonMockClientHandler(HttpStatusCode code, string response)
			=> CreateMockClientHandler(code, response, "application/json");

		internal static Mock<HttpMessageHandler> CreateHtmlMockClientHandler(HttpStatusCode code, string response)
			=> CreateMockClientHandler(code, response, "text/html");

		internal static Mock<HttpMessageHandler> CreateResourceMockClientHandler(HttpStatusCode code, string resourceName)
			=> CreateMockClientHandler(code, new StreamContent(typeof(TestCommon).Assembly.GetManifestResourceStream(resourceName)));

		internal static string[] GetResourceNames()
		{
			return typeof(TestCommon).Assembly.GetManifestResourceNames();
		}
	}
}
