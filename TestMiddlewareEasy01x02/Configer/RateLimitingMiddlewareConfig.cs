namespace TestMiddlewareEasy01x02.Configer
{
    public class RateLimitingMiddlewareConfig
    {
        public int TokenLimit { get; set; }//桶容量
        public int TokensPerPeriod { get; set; }//每周期补充令牌
        public int ReplenishmentPeriodSeconds { get; set; }//补充周期（秒）
        public int CleanupThreshold { get; set; } // 清理阈值（分钟）
    }
}
