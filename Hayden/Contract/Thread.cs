using System.Collections.Generic;

namespace Hayden.Contract
{
	public interface IPost
	{
		ulong PostNumber { get; }
		string Content { get; }
		
		bool? ExtensionIsDeleted { get; set; }
		uint UnixTimestamp { get; }
	}
	
	public interface IThread<TPost> where TPost : IPost
	{
		List<TPost> Posts { get; set; }

		TPost OriginalPost { get; }
		string Title { get; }
		bool Archived { get; }
		bool? IsDeleted { get; set; }
	}
}
