### 本文详细讲解了如何在 AspNetCore 中编写自定义中间件，并实现支持热重载和按 IP 分区的速率限制中间件。
包括基础版和升级版实现、配置文件、PowerShell 测试示例及实验截图。
### 先讲一下怎么编写自定义中间件。
事实上，中间件类就是一个普通的.NET 类，它不需要继承任何父类或者实现任何接口，但是这个类需要有一个构造方法，
且该方法至少要有一个 “ RequestDelegate ” 类型的参数，这个参数用来指向下一个中间件。这个类还需要定义一个名字为 “ Invoke ” 或 “ InvokeAsync ” 的方法，
方法中至少有一个 “ HttpContext ” 类型的参数，方法的返回值必须是 Task 类型。中间件类的 构造方法 和 Invoke(或 InvokeAsync )还可以定义其他参数。
下面是一个简单的自定义中间件（请求耗时中间件）案例：
```csharp
namespace TestMiddlewareEasy01x02.Middlewares
{
    public class RequestDurationMiddleware
    {
        private readonly RequestDelegate _next;
        public RequestDurationMiddleware(RequestDelegate next)
        {
            _next = next;
        }
        public async Task InvokeAsync(HttpContext context)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            context.Response.OnStarting(() =>
            {
                watch.Stop();
                context.Response.Headers["X-Response-Duration-ms"] = watch.ElapsedMilliseconds.ToString();
                return Task.CompletedTask;
            });

            await _next(context);
        }
    }
}

```
运行截图：

![屏幕截图 2026-02-25 102028](https://www.filepunk.top/files/%E5%B1%8F%E5%B9%95%E6%88%AA%E5%9B%BE%202026-02-25%20102028.png)

>注：            await _next(context);后无代码,即表明该中间件无后逻辑
>
在program中使用：
```csharp
app.UseMiddleware<RequestDurationMiddleware>();
```
___
前言讲完了，现在进入我们的重点---速率限制中间件。

## 为何使用速率限制
速率限制可用于管理向应用发出的传入请求流。 实现速率限制的关键原因：

1. 防止滥用：速率限制通过限制用户或客户端在给定时间段内发出的请求数来帮助保护应用免受滥用。 这对于公共 API 尤其重要。
2. 确保公平使用：通过设置限制，所有用户都可以公平地访问资源，防止用户垄断系统。
3. 保护资源：速率限制通过控制可处理的请求数来帮助防止服务器重载，从而防止后端资源过载。
4. 增强安全性：它可以通过限制处理请求的速度来缓解拒绝服务（DoS）攻击的风险，从而使攻击者更难淹没系统。
5. 提高性能：通过控制传入请求的速度，可以维护应用的最佳性能和响应能力，确保更好的用户体验。
6. 成本管理：对于基于使用情况产生成本的服务，速率限制可以通过控制处理的请求量来帮助管理和预测费用

看到这些因素，你认为哪个比较重要？

实时上都很重要，但是 成本管理 最让人肉疼，这是事实，
讲一个网上的案例：某一家公司的验证码注册接口因为，没有加速率限制，导致某一时间段，发送出来大量的短信。
知道吗？如果是使用邮箱的SMTP，这个是可以免费发送邮件的，这也是某些男同胞收藏的学习资料网站（懂得都懂），大都使用邮箱接收验证码的原因；
但如果是发送手机验证码，就需要收费的，大量的短信就意为大量的费用。

再来一个例子，这例子的受害者，就是我本人。事情的起因是这样的，当时学校里面有一场上机的考试（前端HTML），这科我一直使用的ai，老师为了不让我们使用ai 就把80标准端口给禁止了，
所以我就去申请了文心一言的api接口(按量付费)，部署到服务器的非80端口。当时是为了方便快速使用，我就连登录账号都没做，访问就可用，也没加速率限制这些东西。我记得我就只告诉了我的舍友。结果他们又告诉了其他人。就导致太多人使用了。考完之后我去后台一看，按量将近54块。

所以说速率限制真得加。

### 而通常速率限制器有以下四种方法：
1. 固定窗口
2. 滑动窗口
3. 令牌桶
4. 并发

###  令牌桶 加 速率限制分区。

#### 令牌桶：
令牌桶，就是给用户一个桶，这个桶里面有令牌，用户没请求一次消耗一个令牌，当令牌用完请求就被限制了，
而开发者会规定，桶里每个周期补充多少令牌，多少秒为一个周期，补充到桶容量上限就停止补充。

#### 速率限制分区：

速率限制分区将流量划分为单独的“存储桶”，每个分区都获得自己的速率限制计数器。 
这允许比单个全局计数器更精细的控制。 分区“存储桶”由不同的键（如用户 ID、IP 地址或 API 密钥）定义。

### 分区的优点
1. 公平性：一个用户不能耗尽所有人的速率限制
2. 粒度：不同用户/资源的不同限制
3. 安全性：更好地防范有针对性的滥用
4. 分层服务：支持具有不同限制的服务层

>通过分区速率限制，可以精细控制管理 API 流量的方式，同时确保资源分配公平。在我的例子中，如果使用全局限制，而不分区的话，那么多人一起使用，最终会导致所有人的体验感都很差。

## 基础版速率限制中间件

我的习惯一直都是先放配置文件(appsettings.json)：
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "RateLimitingMiddlewareConfig": {
    "TokenLimit": 5,
    "TokensPerPeriod": 1,
    "ReplenishmentPeriodSeconds": 2
  },
  "AllowedHosts": "*"
}

```
### 配置类RateLimitingMiddlewareConfig：
```csharp
namespace TestMiddlewareEasy01x02.Configer
{
    public class RateLimitingMiddlewareConfig
    {
        public int TokenLimit { get; set; }//桶容量
        public int TokensPerPeriod { get; set; }//每周期补充令牌
        public int ReplenishmentPeriodSeconds { get; set; }//补充周期（秒）

    }
}

```


### 中间件类RateLimitingMiddleware.cs：
```csharp
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
        private static readonly ConcurrentDictionary<string, TokenBucketRateLimiter> _buckets = new();

        public RateLimitingMiddleware(RequestDelegate next, IOptionsMonitor<RateLimitingMiddlewareConfig> optionsMonitor)
        {
            _next = next;
            _optionsMonitor = optionsMonitor;
        }
        public async Task InvokeAsync(HttpContext context)
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            //创建该ip的令牌桶
            var limiter = _buckets.GetOrAdd(ip, _ => CreateBucket());
            using var lease = await limiter.AcquireAsync(permitCount: 1);
            var accept = context.Request.Headers["Accept"].ToString();//多态协商accept
            if (!lease.IsAcquired)
            {
                //返回429
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (accept.Contains("application/json"))
                {
                    context.Response.ContentType = "application/json;charset=utf-8";
                    await context.Response.WriteAsync("{\"error\":\"429 Too Many Requests\"}");
                }
                else
                {
                    context.Response.ContentType = "text/html;charset=utf-8";
                    await context.Response.WriteAsync("429 Too Many Requests");
                }
                return;
            }
            await _next(context);

        }
        private TokenBucketRateLimiter CreateBucket()
        { 
            var config = _optionsMonitor.CurrentValue;
            return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = config.TokenLimit,           // 桶容量
                TokensPerPeriod = config.TokensPerPeriod, // 每周期补充令牌数
                ReplenishmentPeriod = TimeSpan.FromSeconds(config.ReplenishmentPeriodSeconds),
                AutoReplenishment = true  // 自动补充
            });
        }
    }
}

```

使用
>builder.Services.AddOptions<RateLimitingMiddlewareConfig>();
>builder.Services.Configure<RateLimitingMiddlewareConfig>(builder.Configuration.GetSection("RateLimitingMiddlewareConfig"));
>app.UseMiddleware<RateLimitingMiddleware>();

测试powershell:
```powershell
$uri = "http://localhost:5026/weatherforecast"

1..10 | ForEach-Object {
    try {
        $r = Invoke-WebRequest $uri -TimeoutSec 2 -UseBasicParsing
        Write-Host "$_ : $($r.StatusCode)" -ForegroundColor Green
    }
    catch {
        $code = $_.Exception.Response.StatusCode.value__
        Write-Host "$_ : $code" -ForegroundColor Red
    }
}
```
powershell测试截图：

![屏幕截图 2026-02-25 110308](https://www.filepunk.top/files/%E5%B1%8F%E5%B9%95%E6%88%AA%E5%9B%BE%202026-02-25%20110308.png)

### 问题1：
注意一下这里：
>private static readonly ConcurrentDictionary<string, TokenBucketRateLimiter> _buckets
这是 static。意味着：整个应用生命周期都存在不支持热更新配置
因为：
>_optionsMonitor.CurrentValue
只在创建桶时读取。

如果修改配置：
1. 老IP桶不会更新参数
2. 新IP才会使用新参数

### 问题2：
因为 _buckets 是 static + 强引用。
如果：
某些 IP 只访问过一次
之后再也不访问
它的 TokenBucketRateLimiter 仍然存在。
长时间运行的服务，IP 会不断累积。

## 升级版速率限制中间件

1. 支持配置热更新（IOptionsMonitor）
2. 支持自动清理长期不用的 IP
3. 无并发重复创建问题
4. 无 TokenBucketRateLimiter 泄漏问题
5. 高并发安全

>该版本需要用到后台托管服务和配置文件读取，
>后台托管服务看这篇：https://blog.liaoxinyuan.top/2025/12/10/%E5%9C%A8AspNetCore%E4%B8%AD%E4%BD%BF%E7%94%A8%E6%89%98%E7%AE%A1%E6%9C%8D%E5%8A%A1/
>配置文件读取：https://blog.liaoxinyuan.top/2025/06/17/ASP-NetCore%E8%AF%BB%E5%8F%96%E9%85%8D%E7%BD%AE%E6%96%87%E4%BB%B6/

appsettings.json配置文件：
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "RateLimitingMiddlewareConfig": {
    "TokenLimit": 5,
    "TokensPerPeriod": 1,
    "ReplenishmentPeriodSeconds": 1,
    "CleanupThreshold": 10
  },
  "AllowedHosts": "*"
}

```
RateLimitingMiddlewareConfig配置类：
```csharp
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

```

RateLimitingMiddleware中间件：
```csharp
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
```

RateLimitCleanupService后台托管服务：
```csharp
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
```

在program.cs中使用
```csharp
builder.Services.AddOptions<RateLimitingMiddlewareConfig>();
builder.Services.Configure<RateLimitingMiddlewareConfig>(builder.Configuration.GetSection("RateLimitingMiddlewareConfig"));
builder.Services.AddHostedService<RateLimitCleanupService>();

app.UseMiddleware<RateLimitingMiddleware>();

```
### 实验：配置热重载
不重启，直接修改
配置文件>    "TokenLimit": 5,修改为    "TokenLimit": 10,
powershell测试：
```powershell
$uri = "http://localhost:5026/weatherforecast"

1..10 | ForEach-Object {
    try {
        $r = Invoke-WebRequest $uri -TimeoutSec 2 -UseBasicParsing
        Write-Host "$_ : $($r.StatusCode)" -ForegroundColor Green
    }
    catch {
        $code = $_.Exception.Response.StatusCode.value__
        Write-Host "$_ : $code" -ForegroundColor Red
    }
}
```
实验截图：

![屏幕截图 2026-02-25 133958](https://www.filepunk.top/files/%E5%B1%8F%E5%B9%95%E6%88%AA%E5%9B%BE%202026-02-25%20133958.png)
