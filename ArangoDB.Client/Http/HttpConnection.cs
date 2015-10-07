﻿using ArangoDB.Client.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace ArangoDB.Client.Http
{
    public class HttpConnection : IHttpConnection
    {
        IArangoDatabase db;

        private static Lazy<HttpClient> httpClientLazily = new Lazy<HttpClient>(() => {
            connectionHandler = new HttpConnectionHandler();
            var proxy = ArangoDatabase.ClientSetting.Proxy;
            connectionHandler.InnerHandler = new HttpClientHandler
            {
                UseProxy = proxy != null,
                Proxy = proxy
            };

            ArangoDatabase.ClientSetting.IsHttpClientInitialied = true;

            var httpClient = new HttpClient(connectionHandler, true);
            httpClient.DefaultRequestHeaders.ExpectContinue = false;
            
            return httpClient;
        });

        private static HttpClient httpClient
        { get { return httpClientLazily.Value; } }

        private static HttpConnectionHandler connectionHandler;

        private static HttpClientHandler clientHandler
        {
            get { return connectionHandler.InnerHandler as HttpClientHandler; }
        }

        public HttpConnection(IArangoDatabase db)
        {
            this.db = db;
        }

        internal static void ConfigureServicePoint(string url)
        {
#if !PORTABLE
            Uri baseUri = new Uri(url);
            var servicePoint = ArangoDatabase.ClientSetting.Proxy != null ? ServicePointManager.FindServicePoint(baseUri
                , ArangoDatabase.ClientSetting.Proxy)
            : ServicePointManager.FindServicePoint(baseUri);
            servicePoint.UseNagleAlgorithm = false;
            servicePoint.Expect100Continue = false;
            servicePoint.ConnectionLimit = 256;
#endif
        }

        public async Task<HttpResponseMessage> SendCommandAsync(HttpMethod method,Uri uri,object data,NetworkCredential credential)
        {
            var requestMessage = new HttpRequestMessage(method,uri);

            string encodedAuthorization = Convert.ToBase64String(Encoding.GetEncoding("ISO-8859-1").GetBytes(credential.UserName + ":" + credential.Password));
            requestMessage.Headers.Add("Authorization", "Basic " + encodedAuthorization);

            if(db.LoggerAvailable)
            {
                db.Log("==============================");
                db.Log(DateTime.Now.ToString());
                db.Log("sending http request:");
                db.Log($"url: {uri.ToString()}");
                db.Log($"method: {method.ToString()}");
                db.Log($"data: {new DocumentSerializer(db).SerializeWithoutReader(data)}");
            }

            if(data!=null)
                requestMessage.Content = new JsonContent(db,data);

            var responseMessage = await httpClient.SendAsync(requestMessage).ConfigureAwait(false);

            if (responseMessage.StatusCode == HttpStatusCode.Unauthorized)
                throw new AuthenticationException($"The user '{credential.UserName}' is not authorized");

            return responseMessage;
        }
    }
}
