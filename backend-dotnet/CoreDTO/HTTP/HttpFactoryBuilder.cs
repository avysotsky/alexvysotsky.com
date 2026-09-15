using System;
using System.Net.Http;

namespace CoreDTO.HTTP
{
    public static class HttpFactoryBuilder
    {
        public static Func<HttpClient> GetFactory(Uri serverUri)
        {
            return () => new HttpClient() { BaseAddress = serverUri };
        }
        public static Func<HttpClient> GetTestClientFactory(HttpClient testHttpClient)
        {
            return () => testHttpClient;
        }
    }
}
