using AspNetCoreRateLimit;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace Demo.RateLimitHeaders.AspNetCore.RateLimitPackage
{
    internal interface IRateLimitHeadersOnlyProcessingStrategy : IProcessingStrategy
    { }

    internal class RateLimitHeadersOnlyProcessingStrategy : ProcessingStrategy, IRateLimitHeadersOnlyProcessingStrategy
    {
        private readonly IRateLimitCounterStore _counterStore;
        private readonly IRateLimitConfiguration _config;

        public RateLimitHeadersOnlyProcessingStrategy(IRateLimitCounterStore counterStore, IRateLimitConfiguration config) : base(config)
        {
            _counterStore = counterStore;
            _config = config;
        }

        public override async Task<RateLimitCounter> ProcessRequestAsync(ClientRequestIdentity requestIdentity, RateLimitRule rule, ICounterKeyBuilder counterKeyBuilder, RateLimitOptions rateLimitOptions, CancellationToken cancellationToken = default)
        {
            string rateLimitCounterId = BuildCounterKey(requestIdentity, rule, counterKeyBuilder, rateLimitOptions);

            RateLimitCounter? rateLimitCounter = await _counterStore.GetAsync(rateLimitCounterId, cancellationToken);
            if (rateLimitCounter.HasValue)
            {
                return new RateLimitCounter
                {
                    Timestamp = rateLimitCounter.Value.Timestamp,
                    Count = rateLimitCounter.Value.Count
                };
            }
            else
            {
                return new RateLimitCounter
                {
                    Timestamp = DateTime.UtcNow,
                    Count = _config.RateIncrementer?.Invoke() ?? 1
            };
            }
        }
    }

    internal class IpRateLimitHeadersMiddleware
    {
        private class RateLimitHeadersState
        {
            public HttpContext Context { get; set; }

            public int Limit { get; set; }

            public int Remaining { get; set; }

            public int Reset { get; set; }

            public string Policy { get; set; } = String.Empty;

            public RateLimitHeadersState(HttpContext context)
            {
                Context = context;
            }
        }

        private readonly RequestDelegate _next;
        private readonly RateLimitOptions _rateLimitOptions;
        private readonly IpRateLimitProcessor _ipRateLimitProcessor;
        private readonly IpRateLimitMiddleware _ipRateLimitMiddleware;

        public IpRateLimitHeadersMiddleware(RequestDelegate next, IRateLimitHeadersOnlyProcessingStrategy processingStrategy, IOptions<IpRateLimitOptions> options, IIpPolicyStore policyStore, IRateLimitConfiguration config, ILogger<IpRateLimitMiddleware> logger)
        {
            _next = next;
            _rateLimitOptions = options?.Value;
            _ipRateLimitProcessor = new IpRateLimitProcessor(options?.Value, policyStore, processingStrategy);
            _ipRateLimitMiddleware = new IpRateLimitMiddleware(next, processingStrategy, options, policyStore, config, logger);
        }

        public async Task Invoke(HttpContext context)
        {
            ClientRequestIdentity identity = await _ipRateLimitMiddleware.ResolveIdentityAsync(context);

            if (!_ipRateLimitProcessor.IsWhitelisted(identity))
            {
                var rateLimitRulesWithCounters = new Dictionary<RateLimitRule, RateLimitCounter>();

                foreach (var rateLimitRule in await _ipRateLimitProcessor.GetMatchingRulesAsync(identity, context.RequestAborted))
                {
                    rateLimitRulesWithCounters.Add(rateLimitRule, await _ipRateLimitProcessor.ProcessRequestAsync(identity, rateLimitRule, context.RequestAborted));
                }

                if (rateLimitRulesWithCounters.Any() && !_rateLimitOptions.DisableRateLimitHeaders)
                {
                    context.Response.OnStarting(SetRateLimitHeaders, state: PrepareRateLimitHeaders(context, rateLimitRulesWithCounters));
                }
            }

            await _next.Invoke(context);

            return;
        }

        private RateLimitHeadersState PrepareRateLimitHeaders(HttpContext context, Dictionary<RateLimitRule, RateLimitCounter> rateLimitRulesWithCounters)
        {
            RateLimitHeadersState rateLimitHeadersState = new RateLimitHeadersState(context);

            var rateLimitHeadersRuleWithCounter = rateLimitRulesWithCounters.OrderByDescending(x => x.Key.PeriodTimespan).FirstOrDefault();
            var rateLimitHeadersRule = rateLimitHeadersRuleWithCounter.Key;
            var rateLimitHeadersCounter = rateLimitHeadersRuleWithCounter.Value;

            rateLimitHeadersState.Limit = (int)rateLimitHeadersRule.Limit;
            rateLimitHeadersState.Remaining = rateLimitHeadersState.Limit - (int)rateLimitHeadersCounter.Count;
            rateLimitHeadersState.Reset = (int)((rateLimitHeadersCounter.Timestamp + (rateLimitHeadersRule.PeriodTimespan ?? rateLimitHeadersRule.Period.ToTimeSpan())) - DateTime.UtcNow).TotalSeconds;
            rateLimitHeadersState.Policy = String.Join(", ", rateLimitRulesWithCounters.Keys.Select(rateLimitRule => $"{(int)rateLimitRule.Limit};w={(int)(rateLimitRule.PeriodTimespan ?? rateLimitRule.Period.ToTimeSpan()).TotalSeconds}"));

            return rateLimitHeadersState;
        }

        private Task SetRateLimitHeaders(object state)
        {
            var rateLimitHeadersState = (RateLimitHeadersState)state;

            rateLimitHeadersState.Context.Response.Headers["RateLimit-Limit"] = rateLimitHeadersState.Limit.ToString(CultureInfo.InvariantCulture);
            rateLimitHeadersState.Context.Response.Headers["RateLimit-Remaining"] = rateLimitHeadersState.Remaining.ToString(CultureInfo.InvariantCulture);
            rateLimitHeadersState.Context.Response.Headers["RateLimit-Reset"] = rateLimitHeadersState.Reset.ToString(CultureInfo.InvariantCulture);
            rateLimitHeadersState.Context.Response.Headers["RateLimit-Policy"] = rateLimitHeadersState.Policy;

            return Task.CompletedTask;
        }
    }
}
