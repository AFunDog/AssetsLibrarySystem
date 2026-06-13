using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AssetsLibrarySystem.Application.Services.AssetSearch;

internal static class HttpClientSearchExtensions
{
    public static Task<HttpResponseMessage> PostAsync(
        this HttpClient http,
        string requestUri,
        CancellationToken cancellationToken)
    {
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        return http.PostAsync(requestUri, content, cancellationToken);
    }
}
