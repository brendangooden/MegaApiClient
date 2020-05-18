#if !NET40
namespace CG.Web.MegaApiClient
{
  using System;
  using System.IO;
  using System.Reflection;
  using System.Text;
  using System.Threading;

  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Threading.Tasks;

  public class WebClient : IWebClient
  {
    private const int DefaultResponseTimeout = Timeout.Infinite;

    private readonly HttpClient httpClient;

    public WebClient(int responseTimeout = DefaultResponseTimeout, ProductInfoHeaderValue userAgent = null)
      : this(responseTimeout, userAgent, null, false)
    {
    }

    internal WebClient(int responseTimeout, ProductInfoHeaderValue userAgent, HttpMessageHandler messageHandler, bool connectionClose)
    {
      this.BufferSize = Options.DefaultBufferSize;
      this.httpClient = messageHandler == null ? new HttpClient() : new HttpClient(messageHandler);
      this.httpClient.Timeout = TimeSpan.FromMilliseconds(responseTimeout);
      this.httpClient.DefaultRequestHeaders.UserAgent.Add(userAgent ?? GenerateUserAgent());
      this.httpClient.DefaultRequestHeaders.ConnectionClose = connectionClose;
    }

    public int BufferSize { get; set; }

    public string PostRequestJson(Uri url, string jsonData)
    {
      using (MemoryStream jsonStream = new MemoryStream(jsonData.ToBytes()))
      {
        return this.PostRequest(url, jsonStream, "application/json");
      }
    }

    public async Task<string> PostRequestRawAsync(Uri url, Stream dataStream)
    {
      return await this.PostRequestAsync(url, dataStream, "application/octet-stream");
    }

    public string PostRequestRaw(Uri url, Stream dataStream)
    {
      return this.PostRequest(url, dataStream, "application/octet-stream");
    }

    public async Task DownloadAsync(Uri url, string downloadLocation, int numberOfParallelDownloads = 0)
    {
      await Downloader.DownloadAsync(httpClient, url, downloadLocation, numberOfParallelDownloads, false);
    }

    public async Task<Stream> GetRequestRawAsync(Uri url)
    {
        return await this.httpClient.GetStreamAsync(url);
    }

    public Stream GetRequestRaw(Uri url)
    {
      return this.httpClient.GetStreamAsync(url).Result;
    }

    private async Task<string> PostRequestAsync(Uri url, Stream dataStream, string contentType)
    {
      using (var content = new StreamContent(dataStream, this.BufferSize))
      {
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using (var response = await this.httpClient.PostAsync(url, content))
        {
          using (var stream = await response.Content.ReadAsStreamAsync())
          {
            using (var streamReader = new StreamReader(stream, Encoding.UTF8))
            {
              return await streamReader.ReadToEndAsync();
            }
          }
        }
      }
    }

    private string PostRequest(Uri url, Stream dataStream, string contentType)
    {
      using (var content = new StreamContent(dataStream, this.BufferSize))
      {
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using (var response = this.httpClient.PostAsync(url, content).Result)
        {
          using (var stream = response.Content.ReadAsStreamAsync().Result)
          {
            using (var streamReader = new StreamReader(stream, Encoding.UTF8))
            {
              return streamReader.ReadToEnd();
            }
          }
        }
      }
    }

    public static ProductInfoHeaderValue GenerateUserAgent()
    {
            return new ProductInfoHeaderValue("MegaApiClient", "1.0.0");
    }
  }
}
#endif
