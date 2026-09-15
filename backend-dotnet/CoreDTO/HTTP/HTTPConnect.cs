using static CoreDTO.Logger.Enums;
using CoreDTO.TextConst;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CoreDTO.HTTP
{
    public abstract class HTTPConnect :  IPinger
    {
        protected const int cancelDelay = 30000;
        protected LogAction logAction;

        public string ServerAddress => serverAddress;
        protected string serverAddress;

        public bool IsReady { get; protected set; } = false;

        public HTTPConnect(string serverAddress, LogAction logAction)
        {
            this.serverAddress = serverAddress;
            this.logAction = logAction;
        }

        public async Task<bool> Ping()
        {
            byte[] result = await GetRequest($"{serverAddress}/{ServiceUrls.ping}");
            if (result.Length == 0)
            {
                return false;
            }
            return true;
        }

        protected async Task<byte[]>GetRequest(string url)
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMilliseconds(cancelDelay);
            using CancellationTokenSource CTS = new CancellationTokenSource(cancelDelay);
            try
            {
                logAction($"[GetRequest] - send request {url}", LogLevel.llFull);
                var response = await client.GetAsync(new Uri(url), CTS.Token);

                if (response.IsSuccessStatusCode)
                {
                    logAction($"[GetRequest] - read responce {url}", LogLevel.llFull);
                    return await response.Content.ReadAsByteArrayAsync();
                }
                return new byte[0];
            }
            catch (Exception ex)
            {
                logAction($"[GetRequest] error to {url} - {ex}", LogLevel.llExceptions);
                return new byte[0];
            }
        }

        protected virtual async Task<byte[]> PostRequest(string url, StringContent data)
        {
            using var client = new HttpClient();
            using CancellationTokenSource CTS = new CancellationTokenSource(cancelDelay);
            try
            {
                logAction($"[PostRequest] - send data {url}", LogLevel.llFull);
                var response = await client.PostAsync(new Uri(url), data, CTS.Token);

                if (response.IsSuccessStatusCode)
                {
                    logAction($"[PostRequest] - read responce {url}", LogLevel.llFull);
                    return await response.Content.ReadAsByteArrayAsync();
                }
                return new byte[0];
            }
            catch (Exception ex)
            {
                logAction($"[PostRequest] error to {url} - {ex}", LogLevel.llExceptions);
                return new byte[0];
            }
        }
        protected async Task<byte[]> SendRequest(string url, StringContent data)
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(url),
                Content = data
            };

            using var client = new HttpClient();
            using CancellationTokenSource CTS = new CancellationTokenSource(cancelDelay);
            try
            {
                logAction($"[PostRequest] - send data {url}", LogLevel.llFull);
                var response = await client.SendAsync(request, CTS.Token);

                if (response.IsSuccessStatusCode)
                {
                    logAction($"[PostRequest] - read responce {url}", LogLevel.llFull);
                    return await response.Content.ReadAsByteArrayAsync();
                }
                return new byte[0];
            }
            catch (Exception ex)
            {
                logAction($"[PostRequest] error to {url} - {ex}", LogLevel.llExceptions);
                return new byte[0];
            }
        }

        public Task Disconnect()
        {
            return Task.CompletedTask;
        }
    }
}
