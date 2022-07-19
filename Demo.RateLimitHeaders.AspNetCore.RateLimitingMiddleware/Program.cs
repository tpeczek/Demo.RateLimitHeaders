using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseHttpsRedirection();

app.UseRateLimiter(new RateLimiterOptions
{
    OnRejected = (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
        }

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        return new ValueTask();
    }
}.AddFixedWindowLimiter("fixed-window", new FixedWindowRateLimiterOptions(
    permitLimit: 5,
    queueProcessingOrder: QueueProcessingOrder.OldestFirst,
    queueLimit: 0,
    window: TimeSpan.FromSeconds(10),
    autoReplenishment: true
)));

app.MapGet("/", context => context.Response.WriteAsync("-- Demo.RateLimitHeaders.AspNetCore.RateLimitingMiddleware --"))
    .RequireRateLimiting("fixed-window");

app.Run();
