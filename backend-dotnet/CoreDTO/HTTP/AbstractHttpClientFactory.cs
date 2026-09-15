using System.Net.Http;

namespace CoreDTO.HTTP
{
    public abstract class AbstractHttpClientFactory
    {
        public abstract HttpClient? GetMediatorClient();
        public abstract HttpClient? GetCrmClient();
        public abstract HttpClient? GetStatisticsClient();
    }
}
