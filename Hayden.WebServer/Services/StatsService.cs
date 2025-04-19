using System;
using System.Threading;
using System.Threading.Tasks;
using Hayden.WebServer.Data;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Hayden.WebServer.Services;

public class StatsService : BackgroundService
{
	public static IDictionary<ushort, BoardStats> CurrentStats { get; private set; }

	private IDataProvider DataProvider { get; }

	public StatsService(IServiceProvider services)
	{
		var scope = services.CreateScope();

		DataProvider = scope.ServiceProvider.GetRequiredService<IDataProvider>();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			CurrentStats = await DataProvider.GetBoardStats();

			await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
		}
	}
}