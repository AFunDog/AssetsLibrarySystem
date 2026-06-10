using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AssetsLibrarySystem.Application.Services.AssetSearch;

internal static class SearchHttpClientExtensions
{
    public static Task<HttpResponseMessage> PostAsync(
        this HttpClient client,
        string? requestUri,
        CancellationToken cancellationToken)
    {
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        return client.PostAsync(requestUri, content, cancellationToken);
    }
}
