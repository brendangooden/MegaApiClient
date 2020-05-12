namespace CG.Web.MegaApiClient
{
  using System;
  using System.IO;
  using System.Threading.Tasks;

  public interface IWebClient
  {
    int BufferSize { get; set; }

    string PostRequestJson(Uri url, string jsonData);

    string PostRequestRaw(Uri url, Stream dataStream);

    Stream GetRequestRaw(Uri url);
    Task<string> PostRequestRawAsync(Uri uri, Stream chunkStream);
  }
}
