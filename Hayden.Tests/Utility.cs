using NUnit.Framework;
using System.IO;
using System.Reflection;

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

		/// <summary>
		/// Retrieves text embedded within the application as a resource stream.
		/// </summary>
		/// <param name="resourceName">The name of the resource.</param>
		/// <returns></returns>
		public static string GetEmbeddedText(string resourceName)
		{
			var assembly = Assembly.GetExecutingAssembly();

			using Stream stream = assembly.GetManifestResourceStream(resourceName);
			using StreamReader reader = new StreamReader(stream);

			return reader.ReadToEnd();
		}
	}
}