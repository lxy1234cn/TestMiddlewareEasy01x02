using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using TestMiddlewareEasy01x02.Configer;

namespace TestMiddlewareEasy01x02.Middlewares
{
    public class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IOptionsMonitor<RateLimitingMiddlewareConfig> _optionsMonitor;
        private readonly ILogger<RateLimitingMiddleware> _logger;

        private static readonly ConcurrentDictionary<string, LimiterEntry> _buckets = new();
        private volatile int _configVersion = 0;

        private static TimeSpan _cleanupThreshold;

        public RateLimitingMiddleware(
            RequestDelegate next,
            IOptionsMonitor<RateLimitingMiddlewareConfig> optionsMonitor,
            ILogger<RateLimitingMiddleware> logger)
        {
            _next = next;
            _optionsMonitor = optionsMonitor;
            _logger = logger;
            _cleanupThreshold = TimeSpan.FromMinutes(_optionsMonitor.CurrentValue.CleanupThreshold);
            _optionsMonitor.OnChange(newConfig =>
            {
                Interlocked.Increment(ref _configVersion);

                _cleanupThreshold = TimeSpan.FromMinutes(newConfig.CleanupThreshold);

                _logger.LogInformation(
                    "Rate limit config changed. Version: {Version}, CleanupThreshold: {Threshold}",
                    _configVersion,
                    _cleanupThreshold);
            });
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var entry = _buckets.AddOrUpdate(
                ip,
                _ => CreateEntry(),
                (_, existing) =>
                {
                    if (existing.Version != _configVersion)
                    {
                        existing.Dispose();
                        return CreateEntry();
                    }

                    existing.Touch();
                    return existing;
                });

            using var lease = await entry.Limiter.AcquireAsync(1);

            if (!lease.IsAcquired)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "application/json;charset=utf-8";
                await context.Response.WriteAsync("{\"error\":\"429 Too Many Requests\"}");
                return;
            }

            await _next(context);
        }

        private LimiterEntry CreateEntry()
        {
            var config = _optionsMonitor.CurrentValue;

            var limiter = new TokenBucketRateLimiter(
                new TokenBucketRateLimiterOptions
                {
                    TokenLimit = config.TokenLimit,
                    TokensPerPeriod = config.TokensPerPeriod,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(config.ReplenishmentPeriodSeconds),
                    AutoReplenishment = true
                });

            return new LimiterEntry(limiter, _configVersion);
        }

        //  清理方法（供后台任务调用）
        public static void Cleanup()
        {
            var now = DateTime.UtcNow;

            foreach (var pair in _buckets)
            {
                if (now - pair.Value.LastAccessTime > _cleanupThreshold)
                {
                    if (_buckets.TryRemove(pair.Key, out var removed))
                    {
                        removed.Dispose();
                    }
                }
            }
        }
    }

    internal class LimiterEntry : IDisposable
    {
        public TokenBucketRateLimiter Limiter { get; }
        public DateTime LastAccessTime { get; private set; }
        public int Version { get; }

        public LimiterEntry(TokenBucketRateLimiter limiter, int version)
        {
            Limiter = limiter;
            Version = version;
            Touch();
        }

        public void Touch()
        {
            LastAccessTime = DateTime.UtcNow;
        }

        public void Dispose()
        {
            Limiter.Dispose();
        }
    }
}