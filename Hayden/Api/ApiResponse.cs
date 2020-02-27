namespace Hayden.Api
{
	/// <summary>
	/// The type of response.
	/// </summary>
	public enum ResponseType
	{
		Ok,
		NotModified,
		NotFound
	}

	/// <summary>
	/// An object containing the result of an API request.
	/// </summary>
	/// <typeparam name="T">The type of data associated with the response.</typeparam>
	public class ApiResponse<T>
	{
		/// <summary>
		/// The type of response.
		/// </summary>
		public ResponseType ResponseType { get; set; }

		/// <summary>
		/// The data returned from the API, depending on the response type.
		/// </summary>
		public T Data { get; set; }

		public ApiResponse(ResponseType responseType, T data)
		{
			ResponseType = responseType;
			Data = data;
		}
	}
}