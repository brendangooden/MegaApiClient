namespace CG.Web.MegaApiClient
{
  using System;
  using System.Collections.Concurrent;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Net;
  using System.Net.Http;
  using System.Text;
  using System.Threading.Tasks;

  internal static class Extensions
  {
    private static readonly DateTime EpochStart = new DateTime(1970, 1, 1, 0, 0, 0, 0);

    public static string ToBase64(this byte[] data)
    {
      StringBuilder sb = new StringBuilder();
      sb.Append(Convert.ToBase64String(data));
      sb.Replace('+', '-');
      sb.Replace('/', '_');
      sb.Replace("=", string.Empty);

      return sb.ToString();
    }

    public static byte[] FromBase64(this string data)
    {
      StringBuilder sb = new StringBuilder();
      sb.Append(data);
      sb.Append(string.Empty.PadRight((4 - data.Length % 4) % 4, '='));
      sb.Replace('-', '+');
      sb.Replace('_', '/');
      sb.Replace(",", string.Empty);

      return Convert.FromBase64String(sb.ToString());
    }

    public static string ToUTF8String(this byte[] data)
    {
      return Encoding.UTF8.GetString(data);
    }

    public static byte[] ToBytes(this string data)
    {
      return Encoding.UTF8.GetBytes(data);
    }

    public static byte[] ToBytesPassword(this string data)
    { 
      // Store bytes characters in uint array
      // discards bits 8-31 of multibyte characters for backwards compatibility
      var array = new uint[(data.Length + 3) >> 2];
      for (var i = 0; i < data.Length; i++)
      {
        array[i >> 2] |= (uint)(data[i] << (24 - (i & 3) * 8));
      }

      return array.SelectMany(x =>
      {
        var bytes = BitConverter.GetBytes(x);
        if (BitConverter.IsLittleEndian)
        {
          Array.Reverse(bytes);
        }

        return bytes;
      }).ToArray();
    }

    public static T[] CopySubArray<T>(this T[] source, int length, int offset = 0)
    {
      T[] result = new T[length];
      while (--length >= 0)
      {
        if (source.Length > offset + length)
        {
          result[length] = source[offset + length];
        }
      }

      return result;
    }

    public static BigInteger FromMPINumber(this byte[] data)
    {
      // First 2 bytes defines the size of the component
      int dataLength = (data[0] * 256 + data[1] + 7) / 8;

      byte[] result = new byte[dataLength];
      Array.Copy(data, 2, result, 0, result.Length);

      return new BigInteger(result);
    }

    public static DateTime ToDateTime(this long seconds)
    {
      return EpochStart.AddSeconds(seconds).ToLocalTime();
    }

    public static long ToEpoch(this DateTime datetime)
    {
      return (long)datetime.ToUniversalTime().Subtract(EpochStart).TotalSeconds;
    }

    public static long DeserializeToLong(this byte[] data, int index, int length)
    {
      byte p = data[index];

      long result = 0;

      if ((p > sizeof(UInt64)) || (p >= length))
      {
        throw new ArgumentException("Invalid value");
      }


      while (p > 0)
      {
        result = (result << 8) + data[index + p--];
      }

      return result;
    }

    public static byte[] SerializeToBytes(this long data)
    {
      byte[] result = new byte[sizeof(long) + 1];

      byte p = 0;
      while (data != 0)
      {
        result[++p] = (byte)data;
        data >>= 8;
      }

      result[0] = p;
      Array.Resize(ref result, result[0] + 1);

      return result;
    }
  }

  public class DownloadResult
  {
    public long Size { get; set; }
    public String FilePath { get; set; }
    public TimeSpan TimeTaken { get; set; }
    public int ParallelDownloads { get; set; }
  }
  internal class Range
  {
    public long Start { get; set; }
    public long End { get; set; }
  }

  public static class Downloader
  {
    static Downloader()
    {
      ServicePointManager.Expect100Continue = false;
      ServicePointManager.DefaultConnectionLimit = 100;
      ServicePointManager.MaxServicePointIdleTime = 1000;

    }
    public static async Task<DownloadResult> DownloadAsync(HttpClient client, Uri fileUrl, String destinationFilePath, int numberOfParallelDownloads = 0, bool validateSSL = false)
    {
      if (!validateSSL)
      {
        ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
      }

      DownloadResult result = new DownloadResult() { FilePath = destinationFilePath };

      //Handle number of parallel downloads  
      if (numberOfParallelDownloads <= 0)
      {
        numberOfParallelDownloads = Environment.ProcessorCount;
      }

      #region Get file size  
      WebRequest webRequest = HttpWebRequest.Create(fileUrl);
      webRequest.Method = "HEAD";
      long responseLength;

      using (WebResponse webResponse = webRequest.GetResponse())
      {
        responseLength = long.Parse(webResponse.Headers.Get("Content-Length"));
        result.Size = responseLength;
      }


      //using (HttpResponseMessage webResponse = await client.GetAsync(fileUrl))
      //{
      //  webResponse.Headers.TryGetValues("Content-Length", out var values);

      //  responseLength = long.Parse(values.First().ToString());
      //  result.Size = responseLength;
      //}
      #endregion

      if (File.Exists(destinationFilePath))
      {
        File.Delete(destinationFilePath);
      }

      using (FileStream destinationStream = new FileStream(destinationFilePath, FileMode.Append))
      {
        ConcurrentDictionary<int, String> tempFilesDictionary = new ConcurrentDictionary<int, String>();

        #region Calculate ranges  
        List<Range> readRanges = new List<Range>();
        for (int chunk = 0; chunk < numberOfParallelDownloads - 1; chunk++)
        {
          var range = new Range()
          {
            Start = chunk * (responseLength / numberOfParallelDownloads),
            End = ((chunk + 1) * (responseLength / numberOfParallelDownloads)) - 1
          };
          readRanges.Add(range);
        }


        readRanges.Add(new Range()
        {
          Start = readRanges.Any() ? readRanges.Last().End + 1 : 0,
          End = responseLength - 1
        });

        #endregion

        DateTime startTime = DateTime.Now;

        #region Parallel download  

        int index = 0;
        Parallel.ForEach(readRanges, new ParallelOptions() { MaxDegreeOfParallelism = numberOfParallelDownloads }, readRange =>
        {
          HttpWebRequest httpWebRequest = HttpWebRequest.Create(fileUrl) as HttpWebRequest;
          httpWebRequest.Method = "GET";
          httpWebRequest.AddRange(readRange.Start, readRange.End);
          httpWebRequest.Headers.Add("User-Agent:MegaApiClient.1.0.0");
        using (HttpWebResponse httpWebResponse = httpWebRequest.GetResponse() as HttpWebResponse)
          {
            String tempFilePath = Path.GetTempFileName();
            using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.Write))
            {
              httpWebResponse.GetResponseStream().CopyTo(fileStream);
              tempFilesDictionary.TryAdd((int)index, tempFilePath);
            }
          }
          index++;

        });

        result.ParallelDownloads = index;

        #endregion

        result.TimeTaken = DateTime.Now.Subtract(startTime);

        #region Merge to single file  
        foreach (var tempFile in tempFilesDictionary.OrderBy(b => b.Key))
        {
          byte[] tempFileBytes = File.ReadAllBytes(tempFile.Value);
          destinationStream.Write(tempFileBytes, 0, tempFileBytes.Length);
          File.Delete(tempFile.Value);
        }
        #endregion


        return result;
      }


    }
  }
}
