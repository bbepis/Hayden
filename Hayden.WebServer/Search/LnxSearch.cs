using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hayden.WebServer.DB.Elasticsearch;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NodaTime;

namespace Hayden.WebServer.Search;

public class LnxSearch : ISearchService
{
	protected ServerSearchConfig Config { get; set; }
	private static HttpClient HttpClient { get; } = new HttpClient();

	public LnxSearch(IOptions<ServerConfig> config)
	{
		Config = config.Value.Search;

		CacheMemoryStream = new MemoryStream();
	}

	public async Task CreateIndex()
	{
		var indexName = Config.IndexName;

		var createString = GenerateIndexCreate(indexName);

		using var response = await HttpClient.PostAsync($"{Config.Endpoint}/indexes",
			new StringContent(GenerateIndexCreate(indexName), Encoding.UTF8, "application/json"));

		if (!response.IsSuccessStatusCode)
		{
			var responseData = JObject.Parse(await response.Content.ReadAsStringAsync());

			if (response.StatusCode == HttpStatusCode.BadRequest &&
			    responseData.Value<string>("data") == "index already exists.")
				return;

			throw new Exception($"Failed to create index: {await response.Content.ReadAsStringAsync()}; {createString}");
		}
	}

	public async Task<SearchResults> PerformSearch(SearchRequest searchRequest)
	{
		var searchParams = new List<string>();

		if (searchRequest.Boards != null && searchRequest.Boards.Length > 0)
		{
			searchParams.Add(
				$"({string.Join(" OR ",
					searchRequest.Boards.Select(x => $"boardId:{x}")
					)})");
		}

		if (!string.IsNullOrWhiteSpace(searchRequest.TextQuery))
			searchParams.Add($"postText:\"{searchRequest.TextQuery.Replace("\"", "")}\"");

		if (!string.IsNullOrWhiteSpace(searchRequest.Subject))
			searchParams.Add($"subject:\"{searchRequest.Subject.Replace("\"", "")}\"");

		if (!string.IsNullOrWhiteSpace(searchRequest.PosterName))
			searchParams.Add($"posterName:\"{searchRequest.PosterName.Replace("\"", "")}\"");

		if (!string.IsNullOrWhiteSpace(searchRequest.PosterTrip))
			searchParams.Add($"tripcode:\"{searchRequest.PosterTrip.Replace("\"", "")}\"");

		if (!string.IsNullOrWhiteSpace(searchRequest.PosterID))
			searchParams.Add($"posterId:\"{searchRequest.PosterID.Replace("\"", "")}\"");

		if (!string.IsNullOrWhiteSpace(searchRequest.Filename))
			searchParams.Add($"mediaFilename:\"{searchRequest.Filename.Replace("\"", "")}\"");

		if (!string.IsNullOrWhiteSpace(searchRequest.FileMD5))
			searchParams.Add($"mediaMd5hash:\"{searchRequest.FileMD5.Replace("\"", "")}\"");

		var searchQuery = new JObject()
		{
			["query"] = new JArray()
			{
				new JObject() {
					["normal"] = new JObject() {
						["ctx"] = string.Join(" AND ", searchParams)
					}
				}
			},
			["limit"] = searchRequest.ResultSize,
			["offset"] = searchRequest.Offset ?? 0,
			["order_by"] = "postDateUtc",
			["sort"] = searchRequest.OrderType == "asc" ? "asc" : "desc"
		};

		using var response = await HttpClient.PostAsync($"{Config.Endpoint}/indexes/{Config.IndexName}/search",
			new StringContent(searchQuery.ToString(Formatting.None), Encoding.UTF8, "application/json"));

		var responseObj = JObject.Parse(await response.Content.ReadAsStringAsync());

		if (responseObj.Value<int>("status") != 200)
			return new SearchResults(Array.Empty<(ushort BoardId, ulong ThreadId, ulong PostId)>(), 0);

		return new SearchResults(
			responseObj["data"]["hits"].Children().Cast<JObject>().Select(hitObj =>
					(hitObj["doc"].Value<ushort>("boardId"),
					hitObj["doc"].Value<ulong>("threadId"),
					hitObj["doc"].Value<ulong>("postId"))).ToArray(),
			responseObj["data"].Value<long>("count")
		);
	}

	private MemoryStream CacheMemoryStream { get; }
	private static UTF8Encoding NoBomEncoding { get; } = new UTF8Encoding(false);
	public async Task IndexBatch(IEnumerable<PostIndex> posts, CancellationToken token = default)
	{
		CacheMemoryStream.SetLength(0);

		using var streamWriter = new StreamWriter(CacheMemoryStream, NoBomEncoding, leaveOpen: true);
		using var jsonWriter = new JsonTextWriter(streamWriter);

		Common.JsonSerializer.Serialize(streamWriter, posts.Select(x => new
		{
			postText = x.PostRawText ?? string.Empty,
			postId = x.PostId,
			threadId = x.ThreadId,
			boardId = x.BoardId,
			subject = x.Subject ?? string.Empty,
			posterName = x.PosterName ?? string.Empty,
			tripcode = x.Tripcode ?? string.Empty,
			posterId = x.PosterID ?? string.Empty,
			mediaFilename = x.MediaFilename ?? string.Empty,
			mediaMd5hash = x.MediaMd5HashBase64 ?? string.Empty,
			postDateUtc = Instant.FromDateTimeUtc(new DateTime(x.PostDateUtc.Ticks, DateTimeKind.Utc)).ToUnixTimeSeconds(),
			isOp = x.IsOp ? 1 : 0,
			isDeleted = x.IsDeleted ? 1 : 0,
		}));

		jsonWriter.Flush();

		CacheMemoryStream.Position = 0;

		//var str = NoBomEncoding.GetString(CacheMemoryStream.ToArray());
		//var content = new StringContent(str, NoBomEncoding, "application/json");

		var content = new StreamContent(CacheMemoryStream);
		content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

		using var response = await HttpClient.PostAsync($"{Config.Endpoint}/indexes/{Config.IndexName}/documents", content);

		if (!response.IsSuccessStatusCode)
		{
			Console.WriteLine(NoBomEncoding.GetString(CacheMemoryStream.ToArray()));
			throw new Exception($"Failed to index documents: {await response.Content.ReadAsStringAsync()}");
		}
	}

	public async Task Commit()
	{
		using var commitResponse = await HttpClient.PostAsync($"{Config.Endpoint}/indexes/{Config.IndexName}/commit", null);

		if (!commitResponse.IsSuccessStatusCode)
			throw new Exception($"Failed to commit: {await commitResponse.Content.ReadAsStringAsync()}");
	}

	#region Query generation

	private static string GenerateIndexCreate(string indexName)
	{
		var fieldsObject = new JObject();

		void AddField(string name, string type, bool stored, bool? indexed = null, bool? fast = null)
		{
			var fieldObject = new JObject()
			{
				["type"] = type,
				["stored"] = stored
			};

			if (indexed.HasValue)
				fieldObject["indexed"] = indexed.Value;
			if (fast.HasValue && fast.Value)
				fieldObject["fast"] = true;

			fieldsObject[name] = fieldObject;
		}

		AddField("postText", "text", false);
		AddField("postId", "u64", true, false, false);
		AddField("threadId", "u64", true, false, false);
		AddField("boardId", "u64", true, true, false);
		
		AddField("subject", "text", false);
		AddField("posterName", "text", false);
		AddField("tripcode", "text", false);
		AddField("posterId", "text", false);
		AddField("mediaFilename", "text", false);
		AddField("mediaMd5Hash", "text", false);

		AddField("postDateUtc", "u64", false, false, true);
		AddField("isOp", "u64", false, false, true);
		AddField("isDeleted", "u64", false, false, true);

		return new JObject
		{
			["override_if_exists"] = false,
			["index"] = new JObject()
			{
				["name"] = indexName,
				["storage_type"] = "filesystem",
				["fields"] = fieldsObject,
				["boost_fields"] = new JObject(),
				["reader_threads"] = 12,
				["max_concurrency"] = 2,
				["writer_buffer"] = 30_000_000,
				["writer_threads"] = 2,
				["set_conjunction_by_default"] = false,
				["use_fast_fuzzy"] = false,
				["strip_stop_words"] = false,
				["auto_commit"] = 0,
			}
		}.ToString(Formatting.None);
	}

	#endregion
}