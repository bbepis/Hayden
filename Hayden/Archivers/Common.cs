using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hayden.Config;

namespace Hayden
{
	public class QueuedImageDownload
	{
		public Uri DownloadUri { get; set; }

		public string DownloadPath { get; set; }

		public QueuedImageDownload() { }

		public QueuedImageDownload(Uri downloadUri, string downloadPath)
		{
			DownloadUri = downloadUri;
			DownloadPath = downloadPath;
		}

		protected bool Equals(QueuedImageDownload other)
		{
			return DownloadUri.AbsolutePath == other.DownloadUri.AbsolutePath
				   && DownloadPath == other.DownloadPath;
		}

		public override bool Equals(object obj)
		{
			if (ReferenceEquals(null, obj))
				return false;
			if (ReferenceEquals(this, obj))
				return true;
			if (obj.GetType() != GetType())
				return false;
			return Equals((QueuedImageDownload)obj);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				return ((DownloadUri != null ? DownloadUri.GetHashCode() : 0) * 397) ^ (DownloadPath != null ? DownloadPath.GetHashCode() : 0);
			}
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
