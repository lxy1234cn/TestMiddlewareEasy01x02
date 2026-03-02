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
