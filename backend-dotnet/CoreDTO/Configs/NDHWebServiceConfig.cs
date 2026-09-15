namespace CoreDTO.Configs
{
    public class NDHWebServiceConfig
    {
        public OrchestratorConfig orchestratorConfig { get; set; }
        public PostgresConfig postgres { get; set; } = new();
        public string AuthorityConnectionString { get; set; } = string.Empty;

        public string? TelegramBotToken { get; set; }
        public string? TelegramChatId { get; set; }

        public string? ContactTelegramChatId { get; set; }


        /// <summary>**Максимум параллельных запросов к Deribit для User/Investor equity**</summary>
        public int AccountEquityMaxParallel { get; set; } = 8;

        /// <summary>**Deribit base URL** (например https://www.deribit.com или https://test.deribit.com)</summary>
        public string DeribitBaseUrl { get; set; } = "https://www.deribit.com";


        public string? SmtpHost { get; set; }
        public int SmtpPort { get; set; } = 587;
        public bool SmtpUseSsl { get; set; } = true;

        public string? SmtpUser { get; set; }
        public string? SmtpPassword { get; set; }

        /// <summary>Куда слать уведомления о новых регистрациях</summary>
        public string? NotificationEmailTo { get; set; }
        public string? ContactEmailTo { get; set; }

    }
}
