using static CoreDTO.Logger.Enums;
using CoreDTO;
using CoreDTO.TextConst;
using CoreDTO.Configs;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Text.Json;

namespace SipCoreDTO.HTTP
{

    public abstract class HttpCRUDClientProxy<T> where T : ResponseDTO, new()
    {
        protected HttpClient client;
        protected const int cancelDelay = 30000;
        protected LogAction logAction;

        public HttpCRUDClientProxy(Func<HttpClient> httpClientFactory, LogAction logAction)
        {
            client = httpClientFactory();
            this.logAction = logAction;
            client.Timeout = TimeSpan.FromMilliseconds(cancelDelay);
        }

        internal async Task<byte[]> GetRequest(string url)
        {            
            using CancellationTokenSource CTS = new CancellationTokenSource(cancelDelay);
            try
            {
                logAction($"[GetRequest] - send request {client.BaseAddress}{url}", LogLevel.llFull);
                var response = await client.GetAsync(new Uri(client.BaseAddress, url), CTS.Token);

                if (response.IsSuccessStatusCode)
                {
                    logAction($"[GetRequest] - read responce {client.BaseAddress}{url}", LogLevel.llFull);
                    return await response.Content.ReadAsByteArrayAsync();
                }
                return new byte[0];
            }
            catch (Exception ex)
            {
                logAction($"[GetRequest][{LogHashTags.externalServiceError}] error to {client.BaseAddress}{url}; {ex}", LogLevel.llExceptions);
                return new byte[0];
            }
        }
        internal async Task<byte[]> SendRequest(string url, StringContent data, HttpMethod httpMethod)
        {
            HttpRequestMessage request = new HttpRequestMessage
            {
                Method = httpMethod,
                RequestUri = new Uri(client.BaseAddress,url),
                Content = data
            };

            using CancellationTokenSource CTS = new CancellationTokenSource(cancelDelay);
            try
            {
                logAction($"[PostRequest] - send data {client.BaseAddress}{url}", LogLevel.llFull);
                var response = await client.SendAsync(request, CTS.Token);

                if (response.IsSuccessStatusCode)
                {
                    logAction($"[PostRequest] - read responce {client.BaseAddress}{url}", LogLevel.llFull);
                    return await response.Content.ReadAsByteArrayAsync();
                }
                return new byte[0];
            }
            catch (Exception ex)
            {
                logAction($"[PostRequest][{LogHashTags.externalServiceError}] error to {client.BaseAddress}{url}; {ex}", LogLevel.llExceptions);
                return new byte[0];
            }
        }

        protected async virtual Task<T> Get(string url)
        {
            var responce = await GetRequest(url);
            var jsonText = Encoding.UTF8.GetString(responce);
            T result = JsonConfig<T>.DeserializeFromJsonString(jsonText, logAction);

            if (result == null)
            {
                return new T()
                {
                    status = false,
                    message = $"[{LogHashTags.externalServiceError}] {client.BaseAddress}{url} Method [Get] - null deserialized"
                };
            }

            return result;
        }
        protected async virtual Task<Dictionary<string, T>> GetList(string url)
        {
            var responce = await GetRequest(url);
            var jsonText = Encoding.UTF8.GetString(responce);
            var result = JsonConfig<Dictionary<string, T>>.DeserializeFromJsonString(jsonText, logAction);
            if (result == null)
            {
                return new Dictionary<string, T>();
            }

            return result;
        }

        protected async virtual Task<T> Delete(string url, T data)
        {
            var request = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
            var responce = await SendRequest(url, request, HttpMethod.Delete);
            var jsonText = Encoding.UTF8.GetString(responce);
            T result = JsonConfig<T>.DeserializeFromJsonString(jsonText, logAction);

            if (result == null)
            {
                return new T()
                {
                    status = false,
                    message = $"[{LogHashTags.externalServiceError}] {client.BaseAddress}{url} Method [Delete] - null deserialized"
                };
            }

            return result;
        }
        protected async virtual Task<T> Put(string url, T data)
        {
            var request = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
            var responce = await SendRequest(url, request, HttpMethod.Put);
            var jsonText = Encoding.UTF8.GetString(responce);
            T result = JsonConfig<T>.DeserializeFromJsonString(jsonText, logAction);

            if (result == null)
            {
                return new T()
                {
                    status = false,
                    message = $"[{LogHashTags.externalServiceError}] {client.BaseAddress}{url} Method [Put] - null deserialized"
                };
            }

            return result;
        }
        protected async virtual Task<T> Post(string url, T data)
        {
            var request = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
            var responce = await SendRequest(url, request, HttpMethod.Post);
            var jsonText = Encoding.UTF8.GetString(responce);
            T result = JsonConfig<T>.DeserializeFromJsonString(jsonText, logAction);

            if (result == null)
            {
                return new T()
                {
                    status = false,
                    message = $"[{LogHashTags.externalServiceError}] {client.BaseAddress}{url} Method [Post] - null deserialized"
                };
            }

            return result;
        }
    }
}
