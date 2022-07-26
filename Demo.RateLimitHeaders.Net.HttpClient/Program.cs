using Demo.RateLimitHeaders.Net.HttpClient;

HttpClient client = new(new RateLimitPolicyHandler());
client.BaseAddress = new("http://localhost:5262");

while (true)
{
    Console.Write("{0:hh:mm:ss}: ", DateTime.UtcNow);

    int nextRequestDelay = 1;

    try
    {
        HttpResponseMessage response = await client.GetAsync("/");
        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine(await response.Content.ReadAsStringAsync());
        }
        else
        {
            Console.Write($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

            string? retryAfter = response.Headers.GetValues("Retry-After").FirstOrDefault();
            if (Int32.TryParse(retryAfter, out nextRequestDelay))
            {
                Console.Write($" | Retry-After: {nextRequestDelay}");
            }

            Console.WriteLine();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex.Message);
    }

    await Task.Delay(TimeSpan.FromSeconds(nextRequestDelay));
}