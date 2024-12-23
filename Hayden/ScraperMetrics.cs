using Prometheus;

namespace Hayden;

public class ScraperMetrics
{
	public ScraperMetrics() : this(Metrics.DefaultFactory) { }

	public MetricFactory MetricFactory { get; private set; }

	public ScraperMetrics(MetricFactory metricFactory)
	{
		MetricFactory = metricFactory;

		TotalPostsScraped = MetricFactory.CreateCounter(
			"posts_scraped_total",
			"Total amount of posts scraped",
			"board");

		TotalThreadsScraped = MetricFactory.CreateCounter(
			"threads_scraped_total",
			"Total amount of threads scraped",
			"board");

		TotalImagesScraped = MetricFactory.CreateCounter(
			"images_scraped_total",
			"Total amount of images scraped",
			"board");

		TotalImageSizeScraped = MetricFactory.CreateCounter(
			"images_scraped_size_total",
			"Total size of all images scraped",
			"board");
	}

	public Counter TotalPostsScraped { get; set; }

	public Counter TotalThreadsScraped { get; set; }

	public Counter TotalImagesScraped { get; set; }

	public Counter TotalImageSizeScraped { get; set; }
}
