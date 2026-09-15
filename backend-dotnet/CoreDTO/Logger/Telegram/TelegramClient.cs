using static CoreDTO.Logger.Enums;
using CoreDTO.HTTP;
using CoreDTO.Configs;
using System;
using System.Net;

namespace CoreDTO.Logger.Telegram
{
    public class TelegramClient : HTTPConnect
    {
        private WebClient webclient = new WebClient();
        private TelegramBotConfig telegramConfig;
        public TelegramClient(TelegramBotConfig telegramConfig, LogAction logAction) : base("", logAction)
        {
            this.telegramConfig = telegramConfig;
        }
        private bool TelegramSendMessage(string apilToken, string destID, string text)
        {
            string urlString = $"{telegramConfig.Url}/bot{apilToken}/sendMessage?chat_id={destID}&text={text}";
            logAction($"[CrmTelegram] TelegramSendMessage <{urlString}>", LogLevel.llExtLogic);

            try
            {
                webclient.DownloadString(urlString);
                return true;
            }
            catch (Exception ex)
            {
                logAction($"Telegram bot error - {ex}", LogLevel.llExceptions);
            }
            return false;
        }
        public bool SendErrorMessage(string message)
        {
            return TelegramSendMessage(telegramConfig.Token,telegramConfig.tech_channel, message);
        }
    }
}
