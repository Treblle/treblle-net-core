using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using System.Diagnostics.CodeAnalysis;
using System.Configuration;
using System.Diagnostics;
using Microsoft.AspNetCore.Http.Extensions;
using Newtonsoft.Json;
using System.Xml.Linq;
using System.Dynamic;
using System;
using System.Linq;
using System.IO;

namespace Treblle.Net.Core
{
    public static class TreblleExtensions
    {
        private static readonly TreblleOptions DefaultOptions = new();
        /// <summary>Adds Treblle middleware.</summary>
        /// <param name="treblleOptions">Treblle options</param>
        public static IApplicationBuilder UseTreblle(this IApplicationBuilder app, TreblleOptions treblleOptions = null)
        {
            if (treblleOptions == null)
            {
                treblleOptions = DefaultOptions;
            }
            if (treblleOptions.ExceptionHandlingEnabled)
            {
                app.UseExceptionHandler(appError => appError.Run(async (context) =>
                {
                    var contextFeature = context.Features.Get<IExceptionHandlerFeature>();

                    if (contextFeature != null)
                    {
                        try
                        {
                            context.Request.EnableBuffering();
                            var apiKey = ConfigurationManager.AppSettings["TreblleApiKey"];
                            var projectId = ConfigurationManager.AppSettings["TreblleProjectId"];
                            var payload = new TrebllePayload();
                            payload.Sdk = "netCore";
                            payload.Version = Environment.Version.ToString();
                            payload.ProjectId = projectId;
                            payload.ApiKey = apiKey;

                            payload.Data.Language.Name = "c#";
                            if (Environment.Version != null)
                            {
                                payload.Data.Language.Version = Environment.Version.Major.ToString() + "." + Environment.Version.Minor.ToString() + "." + Environment.Version.Revision.ToString();
                            }
                            else
                            {
                                payload.Data.Language.Version = String.Empty;
                            }

                            payload.Data.Server.Ip = context.GetServerVariable("LOCAL_ADDR");
                            payload.Data.Server.Timezone = (!String.IsNullOrEmpty(TimeZone.CurrentTimeZone.StandardName)) ? TimeZone.CurrentTimeZone.StandardName : "UTC";
                            payload.Data.Server.Software = context.GetServerVariable("SERVER_SOFTWARE");
                            payload.Data.Server.Signature = null;
                            payload.Data.Server.Protocol = context.GetServerVariable("SERVER_PROTOCOL");

                            payload.Data.Server.Os.Name = Environment.OSVersion.ToString();
                            payload.Data.Server.Os.Release = Environment.OSVersion.Version.ToString();
                            payload.Data.Server.Os.Architecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");

                            payload.Data.Request.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                            payload.Data.Request.Ip = context.GetServerVariable("REMOTE_ADDR");
                            payload.Data.Request.Url = UriHelper.GetDisplayUrl(context.Request);
                            payload.Data.Request.UserAgent = context.Request.Headers?["User-Agent"].ToString();
                            payload.Data.Request.Method = context.Request.Method.ToString();

                            payload.Data.Request.Body = null;

                            try
                            {
                                if (context.Request.ContentType != null)
                                {
                                    var requestReader = new StreamReader(context.Request.Body);
                                    var bodyData = await requestReader.ReadToEndAsync();
                                    if (context.Request.ContentType.Contains("application/json"))
                                    {
                                        payload.Data.Request.Body = JsonConvert.DeserializeObject<dynamic>(bodyData);
                                    }
                                    else if (context.Request.ContentType.Contains("text/plain"))
                                    {
                                        payload.Data.Request.Body = bodyData;
                                    }
                                    else if (context.Request.ContentType.Contains("application/xml"))
                                    {

                                        XDocument doc = XDocument.Parse(bodyData);
                                        string jsonText = JsonConvert.SerializeXNode(doc);
                                        payload.Data.Request.Body = JsonConvert.DeserializeObject<ExpandoObject>(jsonText);
                                    }
                                    else if (context.Request.HasFormContentType)
                                    {
                                        payload.Data.Request.Body = context.Request.Form;
                                    }
                                    context.Request.Body.Position = 0;
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.LogMessage("An error occured while attempting to read request body. --- Exception message: " + ex.Message, LogMessageType.Error);
                            }

                            if (context.Request.Headers != null)
                            {
                                try
                                {
                                    payload.Data.Request.Headers = context.Request.Headers.ToDictionary(x => x.Key, x => String.Join(";", x.Value));
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogMessage("An error occured while attempting to read request headers. --- Exception message: " + ex.Message, LogMessageType.Error);
                                }
                            }

                            if (context.Response.Headers != null)
                            {
                                try
                                {
                                    payload.Data.Response.Headers = context.Response.Headers.ToDictionary(x => x.Key, x => string.Join(";", x.Value));
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogMessage("An error occured while attempting to read response headers. --- Exception message: " + ex.Message, LogMessageType.Error);
                                }
                            }

                            var error = new Error();
                            error.Source = "onException";
                            error.Type = contextFeature.Error.GetType().Name;
                            error.Message = contextFeature.Error.Message;
                            error.File = null;
                            error.Line = 0;

                            var stackTrace = new StackTrace(contextFeature.Error, true);
                            if (stackTrace.FrameCount > 0)
                            {
                                var frame = stackTrace.GetFrame(0);
                                if (frame != null)
                                {
                                    var line = frame.GetFileLineNumber();
                                    if (line != null)
                                    {
                                        error.Line = line;
                                    }
                                    var file = frame.GetFileName();
                                    if (file != null)
                                    {
                                        error.File = file;
                                    }
                                }

                            }

                            payload.Data.Errors.Add(error);

                            payload.Data.Response.Code = 500;
                            payload.Data.Response.Size = 0;

                            payload.Data.Response.Body = null;
                            var treblleApiService = new TreblleApiService();
                            treblleApiService.SendPayload(payload);
                        }
                        catch (Exception ex)
                        {
                            if (ex is ConfigurationException)
                            {
                                Logger.LogMessage("An error occured while trying to read the configuration file. Check if app.config is formatted properly. --- Exception message: " + ex.Message, LogMessageType.Error);
                            }
                            else
                            {
                                Logger.LogMessage("An error occured while intercepting response. --- Exception message: " + ex.Message, LogMessageType.Error);
                            }
                        }
                    }
                }));
            }
            app.UseMiddleware<TreblleMiddleware>();

            return app;
        }
    }
}
