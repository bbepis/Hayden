using System;
using System.Threading.Tasks;
using Hayden.Models;

namespace Hayden.Contract
{
	public interface IThreadConsumer : IDisposable
	{
		Task ConsumeThread(Thread thread, string board);

		Task ThreadUntracked(ulong threadId, string board);
	}
}
