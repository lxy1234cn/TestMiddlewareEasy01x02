using Microsoft.AspNetCore.Builder;
using TestMiddlewareEasy01x02.Configer;
using TestMiddlewareEasy01x02.Middlewares;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddOptions<RateLimitingMiddlewareConfig>();
builder.Services.Configure<RateLimitingMiddlewareConfig>(builder.Configuration.GetSection("RateLimitingMiddlewareConfig"));
builder.Services.AddHostedService<RateLimitCleanupService>();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<RateLimitingMiddleware>();

app.UseMiddleware<RequestDurationMiddleware>();

app.UseHttpsRedirection();

app.UseAuthorization();
app.MapGet("/", () => "Hello! " + DateTime.Now.ToString("HH:mm:ss.fff"));
app.MapControllers();

app.Run();
