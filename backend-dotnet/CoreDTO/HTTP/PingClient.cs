using static CoreDTO.Logger.Enums;
using CoreDTO.TextConst;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CoreDTO.HTTP
{
    public class PingClient : IPinger
    {
        protected HttpClient client;
        protected const int cancelDelay = 30000;
        protected LogAction logAction;
        public string ServerAddress => throw new NotImplementedException();
        public PingClient(HttpClient httpClient, LogAction logAction)
        {
            client = httpClient;
            this.logAction = logAction;
            client.Timeout = TimeSpan.FromMilliseconds(cancelDelay);
        }
        public async Task<bool> Ping()
        {
            byte[] result = await GetRequest($"{ServiceUrls.ping}");
            if (result.Length == 0)
            {
                return false;
            }
            return true;
        }
        internal async Task<byte[]> GetRequest(string url)
        {
            using CancellationTokenSource CTS = new CancellationTokenSource(cancelDelay);
            try
            {
                logAction($"[GetRequest] - send request {client.BaseAddress}{url}", LogLevel.llExtLogic);
                var response = await client.GetAsync(new Uri(client.BaseAddress, url), CTS.Token);

                if (response.IsSuccessStatusCode)
                {
                    logAction($"[GetRequest] - read responce {client.BaseAddress}{url}", LogLevel.llExtLogic);
                    return await response.Content.ReadAsByteArrayAsync();
                }
                return new byte[0];
            }
            catch (Exception ex)
            { 
                logAction($"[GetRequest][#external_service_error] error to {client.BaseAddress}{url} {ex}", LogLevel.llExceptions);
                return new byte[0];
            }
        }
    }
}
