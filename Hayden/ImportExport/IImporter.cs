using System.Collections.Generic;
using System.Threading.Tasks;
using Hayden.Models;

namespace Hayden.ImportExport;

public interface IImporter
{
	Task<string[]> GetBoardList();
	IAsyncEnumerable<ThreadPointer> GetThreadList(string board, long? minId = null, long? maxId = null);
	Task<Thread> RetrieveThread(ThreadPointer pointer);
}

public interface IForwardOnlyImporter
{
	IAsyncEnumerable<(ThreadPointer, Thread)> RetrieveThreads(string[] allowedBoards);
}