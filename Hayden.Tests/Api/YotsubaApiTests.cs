using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Moq;
using Moq.Protected;
using NUnit.Framework;

namespace Hayden.Tests.Api
{
	[TestFixture]
	public class YotsubaApiTests
	{
		private Mock<HttpMessageHandler> CreateMockClientHandler(HttpStatusCode code, string response)
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
					Content = response == null ? null : new StringContent(response, Encoding.UTF8, "application/json")
				})
				.Verifiable();

			return handlerMock;
		}

		private YotsubaApi yotsubaApi = new YotsubaApi();

		private HttpClient CreateMockClient(HttpStatusCode code, string response)
			=> new HttpClient(CreateMockClientHandler(code, response).Object);


		#region GetThread Tests

		[Test]
		public async Task GetThread_Handles404NotFound()
		{
			var mockClient = CreateMockClient(HttpStatusCode.NotFound, null);

			var result = await yotsubaApi.GetThread("a", 1234, mockClient);

			Assert.IsNotNull(result);
			Assert.AreEqual(ResponseType.NotFound, result.ResponseType);
		}

		[Test]
		public async Task GetThread_Handles304NotModified()
		{
			var mockClient = CreateMockClient(HttpStatusCode.NotModified, null);

			var result = await yotsubaApi.GetThread("a", 1234, mockClient);

			Assert.IsNotNull(result);
			Assert.AreEqual(ResponseType.NotModified, result.ResponseType);
		}

		[Test]
		public async Task GetThread_ParsesResponseCorrectly()
		{
			const string testResponse =
				@"{
				  ""posts"": [
					    {
					      ""no"": 51971506,
					      ""sticky"": 1,
					      ""closed"": 1,
					      ""now"": ""12/20/15(Sun)20:03:52"",
					      ""name"": ""Anonymous"",
					      ""com"": ""test"",
					      ""filename"": ""RMS"",
					      ""ext"": "".png"",
					      ""w"": 450,
					      ""h"": 399,
					      ""tn_w"": 250,
					      ""tn_h"": 221,
					      ""tim"": 1450659832892,
					      ""time"": 1450659832,
					      ""md5"": ""cEeDnXfLWSsu3+A/HIZkuw=="",
					      ""fsize"": 299699,
					      ""resto"": 0,
					      ""capcode"": ""mod"",
					      ""semantic_url"": ""the-g-wiki"",
					      ""replies"": 1,
					      ""images"": 1,
					      ""unique_ips"": 2
					    }
					]
				}";

			var mockClient = CreateMockClient(HttpStatusCode.OK, testResponse);

			var result = await yotsubaApi.GetThread("a", 1234, mockClient);

			Assert.AreEqual(ResponseType.Ok, result.ResponseType);
			Assert.IsNotNull(result.Data);

			Assert.AreEqual(1, result.Data.Posts.Count);

			var opPost = result.Data.Posts[0];

			Assert.AreEqual(51971506UL, opPost.PostNumber);
			Assert.IsTrue(opPost.Sticky);
			Assert.IsTrue(opPost.Closed);

			// put more here. i'm lazy
		}

		[Test]
		public async Task GetThread_CallsCorrectEndpoint()
		{
			const string board = "a";
			const int threadNumber = 1234;

			var mockHandler = CreateMockClientHandler(HttpStatusCode.NotFound, null);

			await yotsubaApi.GetThread(board, threadNumber, new HttpClient(mockHandler.Object));

			mockHandler.Protected().Verify(
				"SendAsync",
				Times.Exactly(1),
				ItExpr.Is<HttpRequestMessage>(req =>
					req.Method == HttpMethod.Get
					&& req.RequestUri == new Uri($"https://a.4cdn.org/{board}/thread/{threadNumber}.json")
				),
				ItExpr.IsAny<CancellationToken>()
			);
		}

		[Test]
		public async Task GetThread_SetsNotModifiedSinceHeader()
		{
			var mockHandler = CreateMockClientHandler(HttpStatusCode.NotModified, null);
			var client = new HttpClient(mockHandler.Object);

			var baseDateTimeOffset = DateTimeOffset.Parse("12/3/2007 12:00:00 AM -08:00");

			await yotsubaApi.GetThread("a", 1234, client, baseDateTimeOffset);

			mockHandler.Protected().Verify(
				"SendAsync",
				Times.Exactly(1),
				ItExpr.Is<HttpRequestMessage>(req =>
					req.Headers.IfModifiedSince == baseDateTimeOffset
				),
				ItExpr.IsAny<CancellationToken>()
			);
		}

		#endregion

		#region GetBoard Tests

		[Test]
		public async Task GetBoard_Handles404NotFound()
		{
			var mockClient = CreateMockClient(HttpStatusCode.NotFound, null);

			var result = await yotsubaApi.GetBoard("a", mockClient);

			Assert.IsNotNull(result);
			Assert.AreEqual(ResponseType.NotFound, result.ResponseType);
		}

		[Test]
		public async Task GetBoard_Handles304NotModified()
		{
			var mockClient = CreateMockClient(HttpStatusCode.NotModified, null);

			var result = await yotsubaApi.GetBoard("a", mockClient);

			Assert.IsNotNull(result);
			Assert.AreEqual(ResponseType.NotModified, result.ResponseType);
		}

		[Test]
		public async Task GetBoard_ParsesResponseCorrectly()
		{
			const string testResponse =
				@"[{""page"":1,""threads"":[{""no"":51971506,""last_modified"":1576181967,""replies"":1},{""no"":74912296,""last_modified"":1582802960,""replies"":22}]}]";

			var mockClient = CreateMockClient(HttpStatusCode.OK, testResponse);

			var result = await yotsubaApi.GetBoard("a", mockClient);

			Assert.AreEqual(ResponseType.Ok, result.ResponseType);
			Assert.IsNotNull(result.Data);

			Assert.AreEqual(1, result.Data.Length);

			var firstThread = result.Data[0];
			
			Assert.AreEqual(2, result.Data.Length);
			Assert.AreEqual(51971506UL, firstThread.ThreadNumber);
			Assert.AreEqual(1576181967UL, firstThread.LastModified);
		}

		[Test]
		public async Task GetBoard_CallsCorrectEndpoint()
		{
			const string board = "a";

			var mockHandler = CreateMockClientHandler(HttpStatusCode.NotFound, null);

			await yotsubaApi.GetBoard(board, new HttpClient(mockHandler.Object));

			mockHandler.Protected().Verify(
				"SendAsync",
				Times.Exactly(1),
				ItExpr.Is<HttpRequestMessage>(req =>
					req.Method == HttpMethod.Get
					&& req.RequestUri == new Uri($"https://a.4cdn.org/{board}/threads.json")
				),
				ItExpr.IsAny<CancellationToken>()
			);
		}

		[Test]
		public async Task GetBoard_SetsNotModifiedSinceHeader()
		{
			var mockHandler = CreateMockClientHandler(HttpStatusCode.NotModified, null);
			var client = new HttpClient(mockHandler.Object);

			var baseDateTimeOffset = DateTimeOffset.Parse("12/3/2007 12:00:00 AM -08:00");

			await yotsubaApi.GetBoard("a", client, baseDateTimeOffset);

			mockHandler.Protected().Verify(
				"SendAsync",
				Times.Exactly(1),
				ItExpr.Is<HttpRequestMessage>(req =>
					req.Headers.IfModifiedSince == baseDateTimeOffset
				),
				ItExpr.IsAny<CancellationToken>()
			);
		}

		#endregion

		#region GetBoard Tests

		[Test]
		public async Task GetArchive_Handles404NotFound()
		{
			var mockClient = CreateMockClient(HttpStatusCode.NotFound, null);

			var result = await yotsubaApi.GetArchive("a", mockClient);

			Assert.IsNotNull(result);
			Assert.AreEqual(ResponseType.NotFound, result.ResponseType);
		}

		[Test]
		public async Task GetArchive_Handles304NotModified()
		{
			var mockClient = CreateMockClient(HttpStatusCode.NotModified, null);

			var result = await yotsubaApi.GetArchive("a", mockClient);

			Assert.IsNotNull(result);
			Assert.AreEqual(ResponseType.NotModified, result.ResponseType);
		}

		[Test]
		public async Task GetArchive_ParsesResponseCorrectly()
		{
			const string testResponse = @"[74737273,74743759,74747358]";

			var mockClient = CreateMockClient(HttpStatusCode.OK, testResponse);

			var result = await yotsubaApi.GetArchive("a", mockClient);

			Assert.AreEqual(ResponseType.Ok, result.ResponseType);
			Assert.IsNotNull(result.Data);

			Assert.AreEqual(3, result.Data.Length);

			Assert.AreEqual(74737273UL, result.Data[0]);
			Assert.AreEqual(74743759UL, result.Data[1]);
			Assert.AreEqual(74747358UL, result.Data[2]);
		}

		[Test]
		public async Task GetArchive_CallsCorrectEndpoint()
		{
			const string board = "a";

			var mockHandler = CreateMockClientHandler(HttpStatusCode.NotFound, null);

			await yotsubaApi.GetArchive(board, new HttpClient(mockHandler.Object));

			mockHandler.Protected().Verify(
				"SendAsync",
				Times.Exactly(1),
				ItExpr.Is<HttpRequestMessage>(req =>
					req.Method == HttpMethod.Get
					&& req.RequestUri == new Uri($"https://a.4cdn.org/{board}/archive.json")
				),
				ItExpr.IsAny<CancellationToken>()
			);
		}

		[Test]
		public async Task GetArchive_SetsNotModifiedSinceHeader()
		{
			var mockHandler = CreateMockClientHandler(HttpStatusCode.NotModified, null);
			var client = new HttpClient(mockHandler.Object);

			var baseDateTimeOffset = DateTimeOffset.Parse("12/3/2007 12:00:00 AM -08:00");

			await yotsubaApi.GetArchive("a", client, baseDateTimeOffset);

			mockHandler.Protected().Verify(
				"SendAsync",
				Times.Exactly(1),
				ItExpr.Is<HttpRequestMessage>(req =>
					req.Headers.IfModifiedSince == baseDateTimeOffset
				),
				ItExpr.IsAny<CancellationToken>()
			);
		}

		#endregion
	}
}