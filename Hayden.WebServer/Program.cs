using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Hayden.WebServer
{
	public class Program
	{
		public static async Task<int> Main(string[] args)
		{
			var host = CreateHostBuilder(args).Build();

			if (!await Startup.PerformInitialization(host.Services))
				return 1;

			await host.RunAsync();
			return 0;
		}

		public static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.ConfigureWebHostDefaults(webBuilder =>
				{
					webBuilder.UseStartup<Startup>();
				});
	}
}