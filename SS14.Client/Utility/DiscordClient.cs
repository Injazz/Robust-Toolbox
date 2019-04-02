using DiscordRPC;
using System;
using SS14.Client;

namespace SS14.Client.Utility
{
    class RichPresenceClient : IDisposable
    {
        private DiscordRpcClient _client;
        private ServerInfo _info; 

        public RichPresenceClient(ServerInfo info)
        {
            _info = info;
            BasicClient();
               
        }
        private void BasicClient()
        {

            //Create a new client
            _client = new DiscordRpcClient("560482798364917789");

            //Connect
            _client.Initialize();

            //Send a presence. Do this as many times as you want
            _client.SetPresence(new RichPresence()
            { 
                Details = "Testing Rich Presence",
                State = _info.ServerName,
                Assets = new Assets()
                {
                    LargeImageKey = "devstation",
                    LargeImageText = _info.ServerMaxPlayers.ToString() + " players max",
                    SmallImageKey = "logo",
                    SmallImageText = _info.SessionId.ToString(),
                },
            });       
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
