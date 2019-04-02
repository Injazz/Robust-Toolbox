using DiscordRPC;
using DiscordRPC.Unity;
using System;
using SS14.Client;

namespace SS14.Client.Utility
{
    class RichPresenceClient : IDisposable
    {
        private DiscordRpcClient _client;

        /// <summary>
        /// The level of logging to use.
        /// </summary>
        private static DiscordRPC.Logging.LogLevel logLevel = DiscordRPC.Logging.LogLevel.Info;

        /// <summary>
        /// The pipe to connect too.
        /// </summary>
        private static int discordPipe = -1;

        /// <summary>
        /// The current presence to send to discord.
        /// </summary>
        private static RichPresence _presence = new RichPresence()
        { 
            Details = "Testing Rich Presence",
            State = "In Main Menu",
            Assets = new Assets()
            {
                LargeImageKey = "devstation",
                LargeImageText = "placeholder",
                SmallImageKey = "logo",
                SmallImageText = "placeholder",
            },
        };  

        public RichPresenceClient()
        {
            BasicClient();
               
        }
        private void BasicClient()
        {

            //Create a new client
            _client = new DiscordRpcClient("560499552273170473",          //The client ID of your Discord Application
                    pipe: discordPipe,                                          //The pipe number we can locate discord on. If -1, then we will scan.
                    logger: new DiscordRPC.Logging.ConsoleLogger(logLevel, true),          //The loger to get information back from the client.
                    autoEvents: true,                                           //Should the events be automatically called?
                    client: new UnityNamedPipe()                    //The pipe client to use. Required in mono to be changed.
            );

            //Connect
            _client.Initialize();

            //Send a presence. Do this as many times as you want
            _client.SetPresence(_presence);
        }   

        public void Update() 
        {
            _client.Invoke();
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
