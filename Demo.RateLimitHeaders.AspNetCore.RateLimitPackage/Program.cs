using AspNetCoreRateLimit;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();

builder.Services.Configure<IpRateLimitOptions>(options =>
{
    options.EnableEndpointRateLimiting = true;
    options.StackBlockedRequests = false;
    options.HttpStatusCode = 429;
    options.GeneralRules = new List<RateLimitRule>
    {
        new RateLimitRule { Endpoint = "*", Period = "10s", Limit = 5 }
    };
});

builder.Services.AddInMemoryRateLimiting();

builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

var app = builder.Build();

app.UseHttpsRedirection();

app.UseIpRateLimiting();

app.MapGet("/", context => context.Response.WriteAsync("-- Demo.RateLimitHeaders.AspNetCore.RateLimitPackage --"));

app.Run();
