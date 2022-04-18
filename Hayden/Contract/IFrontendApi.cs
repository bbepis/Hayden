using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hayden.Api;
using Hayden.Models;

namespace Hayden.Contract
{
	public interface IFrontendApi<TThread>
	{
		/// <summary>
		/// Value specifying whether or not the frontend supports / has an archive.
		/// </summary>
		bool SupportsArchive { get; }

		/// <summary>
		/// Retrieves a thread and its posts from the frontend API.
		/// </summary>
		/// <param name="board">The board of the thread.</param>
		/// <param name="threadNumber">The post number of the thread.</param>
		/// <param name="client">The <see cref="HttpClient"/> to make this request with.</param>
		/// <param name="modifiedSince">The value to use in the If-Modified-Since header. Returns NotModified if the thread has not been updated since this time.</param>
		/// <param name="cancellationToken">The cancellation token to use with this request.</param>
		Task<ApiResponse<TThread>> GetThread(string board, ulong threadNumber, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves a list of a board's threads from the frontend API.
		/// </summary>
		/// <param name="board">The board of the thread.</param>
		/// <param name="client">The <see cref="HttpClient"/> to make this request with.</param>
		/// <param name="modifiedSince">The value to use in the If-Modified-Since header. Returns NotModified if the thread has not been updated since this time.</param>
		/// <param name="cancellationToken">The cancellation token to use with this request.</param>
		Task<ApiResponse<PageThread[]>> GetBoard(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves a list of a board's archive's threads from the frontend API.
		/// </summary>
		/// <param name="board">The board of the thread.</param>
		/// <param name="client">The <see cref="HttpClient"/> to make this request with.</param>
		/// <param name="modifiedSince">The value to use in the If-Modified-Since header. Returns NotModified if the thread has not been updated since this time.</param>
		/// <param name="cancellationToken">The cancellation token to use with this request.</param>
		Task<ApiResponse<ulong[]>> GetArchive(string board, HttpClient client, DateTimeOffset? modifiedSince = null, CancellationToken cancellationToken = default);
	}

	public interface ISearchableFrontendApi<TThread> : IFrontendApi<TThread>
	{
		Task<(ulong? total, IAsyncEnumerable<(ulong threadId, string board)> enumerable)> PerformSearch(SearchQuery query, HttpClient client, CancellationToken cancellationToken = default);
	}

	public class SearchQuery
	{
		public string Board { get; set; }

		public string TextQuery { get; set; }
	}
}
