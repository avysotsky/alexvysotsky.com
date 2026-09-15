using System;
using System.Net.Http;

namespace CoreDTO.HTTP
{
    public class HttpClientFactory : AbstractHttpClientFactory
    {
        private Uri mediator;
        private Uri crmProxy;
        private Uri stats;

        public HttpClientFactory(string? mediatorAddress, string? crmProxyAddress, string? statsAddress) 
        {
            mediator = string.IsNullOrWhiteSpace(mediatorAddress) ? null : new Uri(mediatorAddress);
            crmProxy = string.IsNullOrWhiteSpace(crmProxyAddress) ? null : new Uri(crmProxyAddress);
            stats = string.IsNullOrWhiteSpace(statsAddress) ? null : new Uri(statsAddress);  
        }

        public override HttpClient? GetCrmClient()
        {
            return crmProxy is null ? null : new() { BaseAddress = crmProxy};
        }

        public override HttpClient? GetMediatorClient()
        {
            return mediator is null ? null : new() { BaseAddress = mediator };
        }

        public override HttpClient? GetStatisticsClient()
        {
            return stats is null ? null : new() { BaseAddress = stats };
        }
    }
}
