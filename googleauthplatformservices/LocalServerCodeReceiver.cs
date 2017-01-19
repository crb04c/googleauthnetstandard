/*
Copyright 2013 Google Inc

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Logging;
using SimpleHttpServer.Service;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text;
using SimpleHttpServer.Model;

namespace Google.Apis.Auth.OAuth2
{
    /// <summary>
    /// OAuth 2.0 verification code receiver that runs a local server on a free port and waits for a call with the 
    /// authorization verification code.
    /// </summary>
    public class LocalServerCodeReceiver : ICodeReceiver
    {
        private static readonly ILogger Logger = ApplicationContext.Logger.ForType<LocalServerCodeReceiver>();

        /// <summary>The call back format. Expects one port parameter.</summary>
        private const string LoopbackCallback = "http://localhost:{0}/authorize/";

        /// <summary>Close HTML tag to return the browser so it will close itself.</summary>
        private const string ClosePageResponse =
@"<html>
  <head><title>OAuth 2.0 Authentication Token Received</title></head>
  <body>
    Received verification code. You may now close this window.
    <script type='text/javascript'>
      // This doesn't work on every browser.
      window.setTimeout(function() {
          window.open('', '_self', ''); 
          window.close(); 
        }, 1000);
      if (window.opener) { window.opener.checkToken(); }
    </script>
  </body>
</html>";

        private int port;
        private string redirectUri;
        public string RedirectUri
        {
            get
            {
                if (!string.IsNullOrEmpty(redirectUri))
                {
                    return redirectUri;
                }

                this.port = GetRandomUnusedPort();
                return redirectUri = string.Format(LoopbackCallback, port);
            }
        }

        public async Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(AuthorizationCodeRequestUrl url,
            CancellationToken taskCancellationToken)
        {
            var authorizationUrl = url.Build().ToString();

            var list = new HttpListener();
            await list.StartTcpRequestListener(this.port);

            Logger.Debug("Open a browser with \"{0}\" URL", authorizationUrl);
            OpenBrowser(authorizationUrl);

            // Wait to get the authorization code response.
            var request = await list.HttpRequestObservable.Timeout(TimeSpan.FromMinutes(1)).FirstAsync();
            var queryString = request.QueryString.Split('&')
                                .Select(p => p.Split('='))
                                .GroupBy(p => p[0], p=>p.Length > 1 ? Uri.UnescapeDataString(p[1]) : null)
                                .ToDictionary(g => g.Key, g=>g.FirstOrDefault());

            // Write a "close" response.
            var response = new HttpReponse();
            using (var writer = new StreamWriter(response.Body, Encoding.UTF8, 512, true))
            {
                writer.WriteLine(ClosePageResponse);
                writer.Flush();
            }
            response.Headers["content-type"] = "text/html; charset=UTF-8";

            await list.HttpReponse(request, response);

            list.StopTcpRequestListener();

            // Create a new response URL with a dictionary that contains all the response query parameters.
            return new AuthorizationCodeResponseUrl(queryString);
        }


        /// <summary>Returns a random, unused port.</summary>
        private static int GetRandomUnusedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        public static void OpenBrowser(string url)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{url}\""));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                // throw 
            }
        }
    }
}
