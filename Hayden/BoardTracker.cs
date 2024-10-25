using Hayden.Contract;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hayden;
public interface IBoardTracker
{
	List<ThreadPointer> DetermineThreadsToCheck(string board, ICollection<ThreadOverviewInfo> threadOverviewInfos);

	void LoadExistingThreadInfo(string board, ExistingThreadInfo existingThreadInfo);
	void StopTrackingThread(ThreadPointer threadPointer);

	// TODO: add method to remove thread pointers once they're stopped being tracked
}

public class ThreadOverviewInfo
{
	public ulong ThreadId;
	public string Subject;
	public string ContentHtml;
	public DateTimeOffset? LastModified;
	public int? ReplyCount;
	public int Position;
}

public class LastModifiedBoardTracker : IBoardTracker
{
	private Dictionary<string, DateTimeOffset> BoardCheckTimes { get; } = new();

	public List<ThreadPointer> DetermineThreadsToCheck(string board, ICollection<ThreadOverviewInfo> threadOverviewInfos)
	{
		if (threadOverviewInfos.Any(x => !x.LastModified.HasValue))
			throw new ArgumentException("Expected all threads to have last modified time info");

		lock (BoardCheckTimes)
		{
			if (!BoardCheckTimes.TryGetValue(board, out var existingCheckTime))
				existingCheckTime = DateTimeOffset.MinValue;

			BoardCheckTimes[board] = threadOverviewInfos.Max(x => x.LastModified!.Value);

			return threadOverviewInfos
				.Where(x => x.LastModified.Value > existingCheckTime)
				.Select(x => new ThreadPointer(board, x.ThreadId))
				.ToList();

			//Log.Verbose("Thread /{board}/{threadId} has changed (timestamp {timestamp}, last {lastCheckTimestamp}, current {currentTimestamp})",
			//	board, thread.ThreadId, thread.LastModified, lastCheckTimestamp, Utility.GetGMTTimestamp(DateTimeOffset.Now));
		}
	}

	public void LoadExistingThreadInfo(string board, ExistingThreadInfo existingThreadInfo)
	{
		lock (BoardCheckTimes)
		{
			if (!BoardCheckTimes.TryGetValue(board, out var existingCheckTime))
				existingCheckTime = DateTimeOffset.MinValue;


			if (existingThreadInfo.LastPostTime > existingCheckTime)
				BoardCheckTimes[board] = existingThreadInfo.LastPostTime;
		}
	}

	public void StopTrackingThread(ThreadPointer threadPointer)
	{
		// no-op, we're tracking on a board-wide basis
	}
}

public class ReplyCountBoardTracker : IBoardTracker
{
	private Dictionary<ThreadPointer, int> ReplyCounts { get; } = new();

	public List<ThreadPointer> DetermineThreadsToCheck(string board, ICollection<ThreadOverviewInfo> threadOverviewInfos)
	{
		if (threadOverviewInfos.Any(x => !x.ReplyCount.HasValue))
			throw new ArgumentException("Expected all threads to have reply count info");

		var threadList = new List<ThreadPointer>();

		lock (ReplyCounts)
		{
			foreach (var thread in threadOverviewInfos)
			{
				var pointer = new ThreadPointer(board, thread.ThreadId);

				var replyCount = thread.ReplyCount!.Value;

				if (ReplyCounts.TryGetValue(pointer, out var lastReplyCount))
				{
					if (replyCount != lastReplyCount)
					{
						threadList.Add(pointer);
						ReplyCounts[pointer] = replyCount;
					}
				}
				else
				{
					threadList.Add(pointer);
					ReplyCounts[pointer] = replyCount;
				}
			}

			return threadList;

			//Log.Verbose("Thread /{board}/{threadId} has changed (timestamp {timestamp}, last {lastCheckTimestamp}, current {currentTimestamp})",
			//	board, thread.ThreadId, thread.LastModified, lastCheckTimestamp, Utility.GetGMTTimestamp(DateTimeOffset.Now));
		}
	}

	public void LoadExistingThreadInfo(string board, ExistingThreadInfo existingThreadInfo)
	{
		var threadPointer = new ThreadPointer(board, existingThreadInfo.ThreadId);

		lock (ReplyCounts)
		{
			if (!ReplyCounts.ContainsKey(threadPointer))
				ReplyCounts[threadPointer] = existingThreadInfo.PostHashes.Count - 1;
		}
	}

	public void StopTrackingThread(ThreadPointer threadPointer)
	{
		lock (ReplyCounts)
		{
			if (ReplyCounts.ContainsKey(threadPointer))
				ReplyCounts.Remove(threadPointer);
		}
	}
}