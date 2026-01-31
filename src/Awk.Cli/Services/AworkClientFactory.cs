using System.Net.Http.Headers;
using Awk.Generated;

namespace Awk.Services;

internal sealed class AworkClientFactory
{
    internal AworkClient Create(string baseUrl, string token)
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(100)
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.UserAgent.ParseAdd("awork-cli/0.1");
        return new AworkClient(http, baseUrl);
    }
}
