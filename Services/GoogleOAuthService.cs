using AppCopyDirecToDrive.Services;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;


namespace AppCopyDirecToDrive.services
{
    public class GoogleOAuthService
    {
        private const string ClientId = "";
        private const string ClientSecret = "";

        private const string OAuthServerEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string TokenServerEndpoint = "https://oauth2.googleapis.com/token";
        private const string Scope = "https://www.googleapis.com/auth/drive";

        private const string RedirectUri = "http://localhost:8080/";

        private static string codeVerifier = "";
        //private HttpListener _listener;

        public static void OpenAuthInSystemBrowser()
        {
            codeVerifier = Guid.NewGuid().ToString();
            var codeChallenge = GenerateCodeChallenge(codeVerifier);

            var authUrl = $"{OAuthServerEndpoint}?" +
                $"client_id={ClientId}&" +
                $"redirect_uri={Uri.EscapeDataString(RedirectUri)}&" +
                $"response_type=code&" +
                $"scope={Scope}&" +
                $"code_challenge={codeChallenge}&" +
                $"code_challenge_method=S256&" +
                $"access_type=offline";

            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });
        }

        public static async Task<string> ListenForAuthCode()
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(RedirectUri);
            listener.Start();

            var context = await listener.GetContextAsync();
            var code = context.Request.QueryString["code"];

            // Додаткові перевірки
            if (string.IsNullOrEmpty(code))
                throw new Exception("Код авторизації не отримано");

            // Відповідь для браузера
            var response = context.Response;
            var responseString = """
                                    <html><body>
                                    <script>window.close();</script>
                                    <h1>NICE ONE</h1>
                                    </body></html>
                                    """;
            var buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();

            listener.Stop();
            return code;
        }

        public static async Task<TokenResult> ExchangeCodeForToken(string code)
        {
            using var httpClient = new HttpClient();

            var requestData = new Dictionary<string, string>
            {
                {"client_id", ClientId},
                {"client_secret", ClientSecret},
                {"code", code},
                {"code_verifier", codeVerifier},
                {"redirect_uri", RedirectUri},
                {"grant_type", "authorization_code"}
            };

            var response = await httpClient.PostAsync(TokenServerEndpoint, new FormUrlEncodedContent(requestData));
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var tokens = JsonConvert.DeserializeObject<TokenResult>(content)
                       ?? throw new Exception("Не вдалося десеріалізувати відповідь");

            // Зберігаємо токени
            TokenManager.SaveTokens(tokens);

            return tokens;
        }

        public static async Task<TokenResult> RefreshTokenAsync(string refreshToken)
        {
            using var httpClient = new HttpClient();

            var refreshParams = new Dictionary<string, string>
            {
                { "client_id", ClientId },
                { "client_secret", ClientSecret },
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken }
            };

            var response = await httpClient.PostAsync(
                TokenServerEndpoint,
                new FormUrlEncodedContent(refreshParams)
            );

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var tokens = JsonConvert.DeserializeObject<TokenResult>(content)
                       ?? throw new Exception("Не вдалося десеріалізувати відповідь");

            // Оновлюємо збережені токени
            TokenManager.SaveTokens(tokens);

            return tokens;
        }


        private static string GenerateCodeChallenge(string codeVerifier)
        {
            using var sha256 = SHA256.Create();
            var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
            return Convert.ToBase64String(challengeBytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public static DriveService CreateDriveService(string accessToken)
        {
            var initializer = new BaseClientService.Initializer()
            {
                HttpClientInitializer = GoogleCredential.FromAccessToken(accessToken),
                ApplicationName = "MyUploaderApp"
            };

            return new DriveService(initializer);
        }
    }
}

