using NUnit.Framework;

namespace Hayden.Tests
{
	public static class Utility
	{
		public static TestCaseData CreateTestCaseData(string name, params object[] arguments)
		{
			var data = new TestCaseData(arguments);
			data.SetName(name);
			return data;
		}
	}
}