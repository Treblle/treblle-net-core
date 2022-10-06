using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Newtonsoft.Json;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;


namespace Treblle.Net.Core
{
    public class TreblleMiddlewareMetadata
    {
        // add configuration properties if needed
    }

    public static class TreblleMiddlewareExtensions
    {
        /// <summary>Adds treblle to the route.</summary>
        public static RouteHandlerBuilder UseTreblle(this RouteHandlerBuilder builder)
        {
            builder.WithMetadata(new TreblleMiddlewareMetadata());
            return builder;
        }
    }

    public class TreblleMiddleware
    {
        private readonly RequestDelegate _next;

        private string _apiKey = "";

        private string _projectId = "";

        private TreblleApiService _treblleApiService;
        private Stopwatch _stopwatch = new Stopwatch();

        private TrebllePayload _payload;


        public TreblleMiddleware(RequestDelegate next)
        {
            _treblleApiService = new TreblleApiService();
            _next = next;
        }
        public async Task Invoke(HttpContext httpContext)
        {
            _payload = new TrebllePayload();
            if (httpContext.GetEndpoint()?.Metadata.GetMetadata<TreblleMiddlewareMetadata>() is { } mutateResponseMetadata)
            {
                httpContext.Request.EnableBuffering();
                await OnRequest(httpContext);

                _payload.Data.Response.Body = null;
                var originalResponseStream = httpContext.Response.Body;
                using (var ms = new MemoryStream())
                {
                    httpContext.Response.Body = ms;
                    await _next(httpContext);

                    if (httpContext.Response.ContentType != null)
                    {
                        if (!httpContext.Response.ContentType.ToString().Contains("application/json"))
                        {
                            Logger.LogMessage("Attempted to intercept response but content type was not valid. Treblle only works on JSON API's.", LogMessageType.Info);
                            return;
                        }
                    }
                    ms.Position = 0;
                    var responseReader = new StreamReader(ms);

                    var responseContent = responseReader.ReadToEnd();
                    _payload.Data.Response.Body = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    _payload.Data.Response.Size = responseContent.Length;

                    ms.Position = 0;

                    await ms.CopyToAsync(originalResponseStream);
                    httpContext.Response.Body = originalResponseStream;

                    if (httpContext.Response.Headers != null)
                    {
                        try
                        {
                            _payload.Data.Response.Headers = httpContext.Response.Headers.ToDictionary(x => x.Key, x => String.Join(";", x.Value));
                        }
                        catch (Exception ex)
                        {
                            Logger.LogMessage("An error occured while attempting to read response headers. --- Exception message: " + ex.Message, LogMessageType.Error);
                        }
                    }

                    _payload.Data.Response.Code = httpContext.Response.StatusCode;
                    _stopwatch.Stop();
                    _payload.Data.Response.LoadTime = _stopwatch.ElapsedMilliseconds / (double)1000;
                    _treblleApiService.SendPayload(_payload);
                }
            }
            else
            {
                await _next(httpContext);
            }

        }

        private async Task OnRequest(HttpContext httpContext)
        {
            try
            {
                _apiKey = ConfigurationManager.AppSettings["TreblleApiKey"];
                _projectId = ConfigurationManager.AppSettings["TreblleProjectId"];

                if (!String.IsNullOrWhiteSpace(_apiKey) && !String.IsNullOrWhiteSpace(_projectId))
                {
                    _stopwatch.Start();

                    _payload.Sdk = "netCore";
                    _payload.Version = Environment.Version.ToString();
                    _payload.ProjectId = _projectId;
                    _payload.ApiKey = _apiKey;

                    _payload.Data.Language.Name = "c#";
                    if (Environment.Version != null)
                    {
                        _payload.Data.Language.Version = Environment.Version.Major.ToString() + "." + Environment.Version.Minor.ToString() + "." + Environment.Version.Revision.ToString();
                    }
                    else
                    {
                        _payload.Data.Language.Version = String.Empty;
                    }

                    _payload.Data.Server.Ip = httpContext.GetServerVariable("LOCAL_ADDR");
                    _payload.Data.Server.Timezone = (!String.IsNullOrEmpty(TimeZone.CurrentTimeZone.StandardName)) ? TimeZone.CurrentTimeZone.StandardName : "UTC";
                    _payload.Data.Server.Software = httpContext.GetServerVariable("SERVER_SOFTWARE");
                    _payload.Data.Server.Signature = null;
                    _payload.Data.Server.Protocol = httpContext.GetServerVariable("SERVER_PROTOCOL");

                    _payload.Data.Server.Os.Name = Environment.OSVersion.ToString();
                    _payload.Data.Server.Os.Release = Environment.OSVersion.Version.ToString();
                    _payload.Data.Server.Os.Architecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");

                    _payload.Data.Request.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    _payload.Data.Request.Ip = httpContext.GetServerVariable("REMOTE_ADDR");
                    _payload.Data.Request.Url = UriHelper.GetDisplayUrl(httpContext.Request);
                    _payload.Data.Request.UserAgent = httpContext.Request.Headers?["User-Agent"].ToString();
                    _payload.Data.Request.Method = httpContext.Request.Method.ToString();

                    _payload.Data.Request.Body = null;

                    try
                    {
                        if (httpContext.Request.ContentType != null)
                        {
                            var requestReader = new StreamReader(httpContext.Request.Body);
                            var bodyData = await requestReader.ReadToEndAsync();
                            if (httpContext.Request.ContentType.Contains("application/json"))
                            {
                                _payload.Data.Request.Body = JsonConvert.DeserializeObject<dynamic>(bodyData);
                            }
                            else if (httpContext.Request.ContentType.Contains("text/plain"))
                            {
                                _payload.Data.Request.Body = bodyData;
                            }
                            else if (httpContext.Request.ContentType.Contains("application/xml"))
                            {

                                XDocument doc = XDocument.Parse(bodyData);
                                string jsonText = JsonConvert.SerializeXNode(doc);
                                _payload.Data.Request.Body = JsonConvert.DeserializeObject<ExpandoObject>(jsonText);
                            }
                            else if (httpContext.Request.HasFormContentType)
                            {
                                _payload.Data.Request.Body = httpContext.Request.Form;
                            }
                            httpContext.Request.Body.Position = 0;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogMessage("An error occured while attempting to read request body. --- Exception message: " + ex.Message, LogMessageType.Error);
                    }

                    if (httpContext.Request.Headers != null)
                    {
                        try
                        {
                            _payload.Data.Request.Headers = httpContext.Request.Headers.ToDictionary(x => x.Key, x => String.Join(";", x.Value));
                        }
                        catch (Exception ex)
                        {
                            Logger.LogMessage("An error occured while attempting to read request headers. --- Exception message: " + ex.Message, LogMessageType.Error);
                        }
                    }
                }
                else
                {
                    if (String.IsNullOrWhiteSpace(_apiKey))
                    {
                        Logger.LogMessage("Treblle API key not set.", LogMessageType.Info);
                    }
                    if (String.IsNullOrWhiteSpace(_projectId))
                    {
                        Logger.LogMessage("Treblle Project ID not set.", LogMessageType.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex is ConfigurationException)
                {
                    Logger.LogMessage("An error occured while trying to read the configuration file. Check if app.config is formatted properly. --- Exception message: " + ex.Message, LogMessageType.Error);
                }
                else
                {
                    Logger.LogMessage("An error occured while intercepting request. --- Exception message: " + ex.Message, LogMessageType.Error);
                }
            }
        }
    }
}
