using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Thread = Hayden.Models.Thread;

namespace Hayden.Contract
{
	public interface IFrontendApi
	{
		/// <summary>
		/// Determines what features and capabilities the imageboard instance has. May or may not return instantly
		/// </summary>
		/// <param name="client"></param>
		/// <returns>An object detailing the capabilities of the imageboard</returns>
		Task<ApiCapabilities> DetermineCapabilitiesAsync(HttpClient client);

		/// <summary>
		/// Retrieves a thread and its posts from the frontend API.
		/// </summary>
		/// <param name="board">The board of the thread.</param>
		/// <param name="threadNumber">The post number of the thread.</param>
		/// <param name="client">The <see cref="HttpClient"/> to make this request with.</param>
		/// <param name="modifiedSince">The value to use in the If-Modified-Since header. Returns NotModified if the thread has not been updated since this time.</param>
		/// <param name="cancellationToken">The cancellation token to use with this request.</param>
		Task<ApiResponse<Thread>> GetThread(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves a list of a board's threads from the frontend API.
		/// </summary>
		/// <param name="board">The board of the thread.</param>
		/// <param name="client">The <see cref="HttpClient"/> to make this request with.</param>
		/// <param name="modifiedSince">The value to use in the If-Modified-Since header. Returns NotModified if the thread has not been updated since this time.</param>
		/// <param name="cancellationToken">The cancellation token to use with this request.</param>
		Task<ApiResponse<ThreadOverviewInfo[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves a list of a board's archive's threads from the frontend API.
		/// </summary>
		/// <param name="board">The board of the thread.</param>
		/// <param name="client">The <see cref="HttpClient"/> to make this request with.</param>
		/// <param name="modifiedSince">The value to use in the If-Modified-Since header. Returns NotModified if the thread has not been updated since this time.</param>
		/// <param name="cancellationToken">The cancellation token to use with this request.</param>
		Task<ApiResponse<ulong[]>> GetArchive(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default);
	}

	public interface ISearchableFrontendApi : IFrontendApi
	{
		Task<(ulong? total, IAsyncEnumerable<ThreadPointer> enumerable)> PerformSearch(SearchQuery query, HttpClient client, CancellationToken cancellationToken = default);
	}

	public interface IPaginatedFrontEndApi : IFrontendApi
	{
		/// <summary>
		/// Retrieves a list of a board's threads from the frontend API.
		/// </summary>
		/// <param name="board">The board of the thread.</param>
		/// <param name="client">The <see cref="HttpClient"/> to make this request with.</param>
		/// <param name="modifiedSince">The value to use in the If-Modified-Since header. Returns NotModified if the thread has not been updated since this time.</param>
		/// <param name="cancellationToken">The cancellation token to use with this request.</param>
		Task<ApiResponse<IAsyncEnumerable<ThreadOverviewInfo>>> GetBoardPaginated(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default);
	}

	public class SearchQuery
	{
		public string Board { get; set; }

		public string TextQuery { get; set; }
	}

	public class ApiCapabilities
	{
		/// <summary>
		/// Value specifying whether the API can return a list of boards on the imageboard
		/// </summary>
		public bool SupportsBoardListing { get; init; }

		/// <summary>
		/// IF <see cref="SupportsBoardListing"/> is true then an array of boards detected, otherwise null
		/// </summary>
		public string[] BoardList { get; init; }

		/// <summary>
		/// Value specifying whether the frontend supports / has an archive.
		/// </summary>
		public bool SupportsArchive { get; init; }

		/// <summary>
		/// Whether the board API can return last modified times on threads
		/// </summary>
		public bool SupportsBoardLastModified { get; init; }

		/// <summary>
		/// Whether the board API can return reply counts on threads
		/// </summary>
		public bool SupportsBoardReplyCount { get; init; }

		/// <summary>
		/// True if posts retain their original IDs when moved to another thread (within the same board), or false if they are given new IDs
		/// </summary>
		public bool MovedPostsRetainIds { get; init; }

		// Need to actually confirm that crystal.cafe conflicting posts have the same data
	}
}
