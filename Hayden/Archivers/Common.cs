using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hayden.Config;

namespace Hayden
{
	public class QueuedImageDownload
	{
		public Uri FullImageUri { get; set; }

		public Uri ThumbnailImageUri { get; set; }

		public Dictionary<string, object> Properties { get; set; }
		
		public Guid Guid { get; set; }

		public QueuedImageDownload(Uri fullImageUri, Uri thumbnailImageUri, Dictionary<string, object> properties)
		{
			FullImageUri = fullImageUri;
			ThumbnailImageUri = thumbnailImageUri;
			Properties = properties;
			Guid = Guid.NewGuid();
		}

		public QueuedImageDownload(Uri fullImageUri, Uri thumbnailImageUri) : this(fullImageUri, thumbnailImageUri, new Dictionary<string, object>()) { }

		public QueuedImageDownload() { }

		public bool TryGetProperty<T>(string key, out T value)
		{
			if (Properties == null)
				throw new InvalidOperationException("Properties object is null");

			if (Properties.TryGetValue(key, out var rawValue))
			{
				if (typeof(T) == rawValue.GetType())
					value = (T)rawValue;
				else
					value = (T)Convert.ChangeType(rawValue, typeof(T));

				return true;
			}

			value = default;
			return false;
		}

		protected bool Equals(QueuedImageDownload other)
		{
			return Equals(FullImageUri, other.FullImageUri) && Equals(ThumbnailImageUri, other.ThumbnailImageUri) && Guid == other.Guid;
		}

		public override bool Equals(object obj)
		{
			if (ReferenceEquals(null, obj)) return false;
			if (ReferenceEquals(this, obj)) return true;
			if (obj.GetType() != this.GetType()) return false;
			return Equals((QueuedImageDownload)obj);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(FullImageUri, ThumbnailImageUri, Guid);
		}

		public static bool operator ==(QueuedImageDownload left, QueuedImageDownload right)
		{
			return Equals(left, right);
		}

		public static bool operator !=(QueuedImageDownload left, QueuedImageDownload right)
		{
			return !Equals(left, right);
		}
	}

	/// <summary>
	/// A struct containing information about a specific thread.
	/// </summary>
	public readonly struct ThreadPointer : IComparable<ThreadPointer>, IEquatable<ThreadPointer>
	{
		public string Board { get; }

		public ulong ThreadId { get; }

		public ThreadPointer(string board, ulong threadId)
		{
			Board = board;
			ThreadId = threadId;
		}

		#region Interface boilerplate

		public int CompareTo(ThreadPointer other)
		{
			int result = Board.CompareTo(other.Board);

			if (result != 0)
				return result;

			return ThreadId.CompareTo(other.ThreadId);
		}

		public bool Equals(ThreadPointer other)
		{
			return Board == other.Board && ThreadId == other.ThreadId;
		}

		public override bool Equals(object obj)
		{
			return obj is ThreadPointer other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Board, ThreadId);
		}

		public static bool operator ==(ThreadPointer left, ThreadPointer right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(ThreadPointer left, ThreadPointer right)
		{
			return !left.Equals(right);
		}

		#endregion
	}

	public enum ThreadUpdateStatus
	{
		Ok,
		Deleted,
		Archived,
		NotModified,
		DoNotArchive,
		Error
	}

	public struct ThreadUpdateTaskResult
	{
		public bool Success { get; set; }
		public IList<QueuedImageDownload> ImageDownloads { get; set; }
		public ThreadUpdateStatus Status { get; set; }
		public int PostCountChange { get; set; }

		public ThreadUpdateTaskResult(bool success, IList<QueuedImageDownload> imageDownloads, ThreadUpdateStatus status, int postCountChange)
		{
			Success = success;
			ImageDownloads = imageDownloads;
			Status = status;
			PostCountChange = postCountChange;
		}
	}

	public class BoardRules
	{
		public Regex ThreadTitleRegex { get; set; }
		public Regex OPContentRegex { get; set; }
		public Regex AnyFilter { get; set; }

		public BoardRules(BoardRulesConfig config)
		{
			if (!string.IsNullOrWhiteSpace(config.ThreadTitleRegexFilter))
			{
				ThreadTitleRegex = new Regex(config.ThreadTitleRegexFilter, RegexOptions.IgnoreCase | RegexOptions.Compiled);
			}

			if (!string.IsNullOrWhiteSpace(config.OPContentRegexFilter))
			{
				OPContentRegex = new Regex(config.OPContentRegexFilter, RegexOptions.IgnoreCase | RegexOptions.Compiled);
			}

			if (!string.IsNullOrWhiteSpace(config.AnyFilter))
			{
				AnyFilter = new Regex(config.AnyFilter, RegexOptions.IgnoreCase | RegexOptions.Compiled);
			}
		}
	}
}
