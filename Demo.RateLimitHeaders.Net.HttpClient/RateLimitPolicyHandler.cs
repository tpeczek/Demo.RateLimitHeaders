using System.Net;
using System.Globalization;
using System.Threading.RateLimiting;
using System.Text.RegularExpressions;

namespace Demo.RateLimitHeaders.Net.HttpClient
{
    internal class RateLimitPolicyHandler : DelegatingHandler
    {
        private string? _rateLimitPolicy;
        private RateLimiter? _rateLimiter;

        private static readonly Regex RATE_LIMIT_POLICY_REGEX = new Regex(@"(\d+);w=(\d+)", RegexOptions.Compiled);

        public RateLimitPolicyHandler() : base(new HttpClientHandler())
        { }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_rateLimiter is not null)
            {
                using var rateLimitLease = await _rateLimiter.WaitAsync(1, cancellationToken);
                if (rateLimitLease.IsAcquired)
                {
                    return await base.SendAsync(request, cancellationToken);
                }

                var rateLimitResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                rateLimitResponse.Content = new StringContent($"Service rate limit policy ({_rateLimitPolicy}) exceeded!");

                if (rateLimitLease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    rateLimitResponse.Headers.Add("Retry-After", ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo));
                }

                return rateLimitResponse;
            }

            var response = await base.SendAsync(request, cancellationToken);

            if (response.Headers.Contains("RateLimit-Policy"))
            {
                _rateLimitPolicy = response.Headers.GetValues("RateLimit-Policy").FirstOrDefault();

                if (_rateLimitPolicy is not null)
                {
                    Match rateLimitPolicyMatch = RATE_LIMIT_POLICY_REGEX.Match(_rateLimitPolicy);

                    if (rateLimitPolicyMatch.Success)
                    {
                        int limit = Int32.Parse(rateLimitPolicyMatch.Groups[1].Value);
                        int window = Int32.Parse(rateLimitPolicyMatch.Groups[2].Value);

                        _rateLimiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions(
                            limit,
                            QueueProcessingOrder.NewestFirst,
                            0,
                            TimeSpan.FromSeconds(window),
                            true
                        ));

                        string? rateLimitRemaining = response.Headers.GetValues("RateLimit-Remaining").FirstOrDefault();
                        if (Int32.TryParse(rateLimitRemaining, out int remaining))
                        {
                            using var rateLimitLease = await _rateLimiter.WaitAsync(limit - remaining, cancellationToken);
                        }
                    }
                }
            }

            return response;
        }
    }
}
