using Hayden.Config;
using Hayden.Consumers.HaydenMysql.DB;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.IO.Abstractions;

namespace Hayden.Consumers.HaydenMysql;
internal class MaintenanceManager
{
	private DbContextOptions<HaydenDbContext> DbContextOptions { get; set; }
	private ConsumerConfig ConsumerConfig { get; set; }

	public MaintenanceManager(ConsumerConfig consumerConfig)
	{
		DbContextOptions = new DbContextOptionsBuilder<HaydenDbContext>()
			.SetupHaydenDb(consumerConfig: consumerConfig)
			.Options;

		ConsumerConfig = consumerConfig;
	}

	public async Task DeleteThread()
	{

	}

	public async Task PerformUpgrade()
	{
		var upgrader = new HaydenDbUpgrader();
		await upgrader.UpgradeAsync(ConsumerConfig, DbContextOptions, new FileSystem(), false);
	}
}
