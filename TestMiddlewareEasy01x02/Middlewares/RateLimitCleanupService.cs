using Microsoft.Extensions.Hosting;

namespace TestMiddlewareEasy01x02.Middlewares
{
    public class RateLimitCleanupService : BackgroundService
    {
        private readonly ILogger<RateLimitCleanupService> _logger;

        public RateLimitCleanupService(ILogger<RateLimitCleanupService> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                RateLimitingMiddleware.Cleanup();
                _logger.LogInformation("Rate limit cleanup executed.");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}