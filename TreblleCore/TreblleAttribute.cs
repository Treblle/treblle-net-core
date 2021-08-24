using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Treblle.Net.Core
{
    public class TreblleAttribute : Attribute, IAsyncAlwaysRunResultFilter, IAsyncActionFilter, IExceptionFilter
    {
        private List<string> sensitiveWords = new List<string>() {
            "password",
            "pwd",
            "secret",
            "password_confirmation",
            "passwordConfirmation",
            "cc",
            "card_number",
            "cardNumber",
            "ccv",
            "ssn",
            "credit_score",
            "creditScore"
        };

        public string ApiKey = "";

        public string ProjectId = "";

        Stopwatch stopwatch = new Stopwatch();

        TrebllePayload payload = new TrebllePayload();
        Data data = new Data();
        Response response = new Response();
        Error error = new Error();
        Request request = new Request();
        Language language = new Language();
        Server server = new Server();
        Os os = new Os();

        public async Task OnActionExecutionAsync(ActionExecutingContext actionContext, ActionExecutionDelegate next)
        {
            try
            {
                ApiKey = ConfigurationManager.AppSettings["TreblleApiKey"];
                ProjectId = ConfigurationManager.AppSettings["TreblleProjectId"];

                if (!String.IsNullOrWhiteSpace(ApiKey) && !String.IsNullOrWhiteSpace(ProjectId))
                {
                    stopwatch.Start();

                    payload.Sdk = "netCore";
                    payload.Version = Environment.Version.ToString();
                    payload.ProjectId = ProjectId;
                    payload.ApiKey = ApiKey;

                    language.Name = "c#";
                    if (Environment.Version != null)
                    {
                        language.Version = Environment.Version.Major.ToString() + "." + Environment.Version.Minor.ToString() + "." + Environment.Version.Revision.ToString();
                    }
                    else
                    {
                        language.Version = String.Empty;
                    }

                    server.Ip = actionContext.HttpContext.GetServerVariable("LOCAL_ADDR");
                    server.Timezone = (!String.IsNullOrEmpty(TimeZone.CurrentTimeZone.StandardName)) ? TimeZone.CurrentTimeZone.StandardName : "UTC";
                    server.Software = actionContext.HttpContext.GetServerVariable("SERVER_SOFTWARE");
                    server.Signature = null;
                    server.Protocol = actionContext.HttpContext.GetServerVariable("SERVER_PROTOCOL");

                    os.Name = Environment.OSVersion.ToString();
                    os.Release = Environment.OSVersion.Version.ToString();
                    os.Architecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");

                    request.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    request.Ip = actionContext.HttpContext.GetServerVariable("REMOTE_ADDR");
                    request.Url = UriHelper.GetDisplayUrl(actionContext.HttpContext.Request);
                    request.UserAgent = actionContext.HttpContext.Request.Headers?["User-Agent"].ToString();
                    request.Method = actionContext.HttpContext.Request.Method.ToString();

                    request.Body = null;

                    if (actionContext.HttpContext.Request.ContentType == "application/json")
                    {
                        string bodyData = actionContext.HttpContext.Request.ReadBodyAsString();
                        request.Body = JsonConvert.DeserializeObject<dynamic>(bodyData);
                    }

                    if (actionContext.HttpContext.Request.Headers != null)
                    {
                        try
                        {
                            request.Headers = actionContext.HttpContext.Request.Headers.ToDictionary(x => x.Key, x => String.Join(";", x.Value));
                        }
                        catch (Exception ex)
                        {
                            Logger.LogMessage("An error occured while attempting to read request headers. --- Exception message: " + ex.Message, LogMessageType.Error);
                        }
                    }
                }
                else
                {
                    if (String.IsNullOrWhiteSpace(ApiKey))
                    {
                        Logger.LogMessage("Treblle API key not set.", LogMessageType.Info);
                    }
                    if (String.IsNullOrWhiteSpace(ProjectId))
                    {
                        Logger.LogMessage("Treblle Project ID not set.", LogMessageType.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                string actionDescriptor = "";
                if (actionContext != null)
                {
                    if (actionContext.ActionDescriptor != null)
                    {
                        if (!String.IsNullOrWhiteSpace(actionContext.ActionDescriptor.DisplayName))
                        {
                            actionDescriptor = " --- At: " + actionContext.ActionDescriptor.DisplayName;
                        }
                    }
                }
                if (ex is ConfigurationException)
                {
                    Logger.LogMessage("An error occured while trying to read the configuration file. Check if app.config is formatted properly. --- Exception message: " + ex.Message, LogMessageType.Error);
                }
                else
                {
                    Logger.LogMessage("An error occured while intercepting request." + actionDescriptor + " --- Exception message: " + ex.Message, LogMessageType.Error);
                }
            }
            await next();
        }

        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            try
            {
                ApiKey = ConfigurationManager.AppSettings["TreblleApiKey"];
                ProjectId = ConfigurationManager.AppSettings["TreblleProjectId"];

                if (!String.IsNullOrWhiteSpace(ApiKey) && !String.IsNullOrWhiteSpace(ProjectId))
                {
                    response.Body = null;
                    var originalBodyStream = context.HttpContext.Response.Body;
                    using (var responseBody = new MemoryStream())
                    {
                        // temporary response body
                        context.HttpContext.Response.Body = responseBody;

                        //Continue down the Middleware pipeline, eventually returning to this class
                        await next();

                        if (context.HttpContext.Response.ContentType != null)
                        {
                            if (!context.HttpContext.Response.ContentType.ToString().Contains("application/json"))
                            {
                                Logger.LogMessage("Attempted to intercept response but content type was not valid. Treblle only works on JSON API's.", LogMessageType.Info);
                                return;
                            }
                        }

                        var bodyData = await context.HttpContext.Response.ReadBodyAsString();
                        response.Body = JsonConvert.DeserializeObject<dynamic>(bodyData);
                        response.Size = responseBody.Length;

                        //Copy the contents of the new memory stream (which contains the response) to the original stream, which is then returned to the client.
                        await responseBody.CopyToAsync(originalBodyStream);
                    }

                    if (context.HttpContext.Response.Headers != null)
                    {
                        try
                        {
                            response.Headers = context.HttpContext.Response.Headers.ToDictionary(x => x.Key, x => String.Join(";", x.Value));
                        }
                        catch (Exception ex)
                        {
                            Logger.LogMessage("An error occured while attempting to read response headers. --- Exception message: " + ex.Message, LogMessageType.Error);
                        }
                    }

                    response.Code = (int)context.HttpContext.Response.StatusCode;

                    // Alternative way of getting response body
                    //var result = context.Result;
                    //if (result is JsonResult json)
                    //{
                    //    response.Body = Newtonsoft.Json.JsonConvert.SerializeObject(json.Value);
                    //}
                    //else if (result is ObjectResult objectResult)
                    //{
                    //    response.Body = objectResult.Value;
                    //}

                    stopwatch.Stop();
                    response.LoadTime = stopwatch.ElapsedMilliseconds / (double)1000;

                    PrepareAndSendJson();
                }
                else
                {
                    if (String.IsNullOrWhiteSpace(ApiKey))
                    {
                        Logger.LogMessage("Treblle API key not set.", LogMessageType.Info);
                    }
                    if (String.IsNullOrWhiteSpace(ProjectId))
                    {
                        Logger.LogMessage("Treblle Project ID not set.", LogMessageType.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                string actionDescriptor = "";
                if (context != null)
                {
                    if (context.ActionDescriptor != null)
                    {
                        if (!String.IsNullOrWhiteSpace(context.ActionDescriptor.DisplayName))
                        {
                            actionDescriptor = " --- At: " + context.ActionDescriptor.DisplayName;
                        }
                    }
                }
                if (ex is ConfigurationException)
                {
                    Logger.LogMessage("An error occured while trying to read the configuration file. Check if app.config is formatted properly. --- Exception message: " + ex.Message, LogMessageType.Error);
                }
                else
                {
                    Logger.LogMessage("An error occured while intercepting response." + actionDescriptor + " --- Exception message: " + ex.Message, LogMessageType.Error);
                }
                await next();
            }
        }

        public void OnException(ExceptionContext context)
        {
            try
            {
                ApiKey = ConfigurationManager.AppSettings["TreblleApiKey"];
                ProjectId = ConfigurationManager.AppSettings["TreblleProjectId"];

                if (!String.IsNullOrWhiteSpace(ApiKey) && !String.IsNullOrWhiteSpace(ProjectId))
                {
                    data.Errors = new List<Error>();

                    if (context.HttpContext.Response.Headers != null)
                    {
                        try
                        {
                            response.Headers = context.HttpContext.Response.Headers.ToDictionary(x => x.Key, x => String.Join(";", x.Value));
                        }
                        catch (Exception ex)
                        {
                            Logger.LogMessage("An error occured while attempting to read response headers. --- Exception message: " + ex.Message, LogMessageType.Error);
                        }
                    }

                    if (context.Exception != null)
                    {
                        error.Source = "onException";
                        error.Type = context.Exception.GetType().Name;
                        error.Message = context.Exception.Message;
                        error.File = null;
                        error.Line = 0;

                        var stackTrace = new StackTrace(context.Exception, true);
                        if (stackTrace != null)
                        {
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
                        }

                        data.Errors.Add(error);

                        response.Code = 500;
                        response.Size = 0;

                        response.Body = null;
                    }
                    else
                    {

                        response.Code = (int)context.HttpContext.Response.StatusCode;
                        response.Size = context.HttpContext.Response.Headers.ContentLength.HasValue ? context.HttpContext.Response.Headers.ContentLength.Value : 0;

                        response.Body = null;

                        if (context.HttpContext.Response.ContentType != null)
                        {
                            if (!context.HttpContext.Response.ContentType.ToString().Contains("application/json"))
                            {
                                Logger.LogMessage("Attempted to intercept response but content type was not valid. Treblle only works on JSON API's.", LogMessageType.Info);
                                return;
                            }
                        }

                        var result = context.Result;
                        if (result is JsonResult json)
                        {
                            response.Body = Newtonsoft.Json.JsonConvert.SerializeObject(json.Value);
                        }
                        else if (result is ObjectResult objectResult)
                        {
                            response.Body = objectResult.Value;
                        }

                    }

                    stopwatch.Stop();
                    response.LoadTime = stopwatch.ElapsedMilliseconds / (double)1000;

                    PrepareAndSendJson();
                }
                else
                {
                    if (String.IsNullOrWhiteSpace(ApiKey))
                    {
                        Logger.LogMessage("Treblle API key not set.", LogMessageType.Info);
                    }
                    if (String.IsNullOrWhiteSpace(ProjectId))
                    {
                        Logger.LogMessage("Treblle Project ID not set.", LogMessageType.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                string actionDescriptor = "";
                if (context != null)
                {
                    if (context.ActionDescriptor != null)
                    {
                        if (!String.IsNullOrWhiteSpace(context.ActionDescriptor.DisplayName))
                        {
                            actionDescriptor = " --- At: " + context.ActionDescriptor.DisplayName;
                        }
                    }
                }
                if (ex is ConfigurationException)
                {
                    Logger.LogMessage("An error occured while trying to read the configuration file. Check if app.config is formatted properly. --- Exception message: " + ex.Message, LogMessageType.Error);
                }
                else
                {
                    Logger.LogMessage("An error occured while intercepting response." + actionDescriptor + " --- Exception message: " + ex.Message, LogMessageType.Error);
                }
            }
        }

        public void PrepareAndSendJson()
        {
            try
            {
                server.Os = os;

                data.Language = language;
                data.Request = request;
                data.Response = response;
                data.Server = server;

                payload.Data = data;

                var json = JsonConvert.SerializeObject(payload);

                var additionalFieldsToMask = System.Configuration.ConfigurationManager.AppSettings["AdditionalFieldsToMask"];
                if (!String.IsNullOrWhiteSpace(additionalFieldsToMask))
                {
                    var additionalFields = additionalFieldsToMask.Split(',');
                    if (additionalFields.Any())
                    {
                        var list = additionalFields.ToList();
                        sensitiveWords.AddRange(list);
                    }
                }

                var maskedJson = json.Mask(sensitiveWords.ToArray(), "*****");

                var httpWebRequest = (HttpWebRequest)WebRequest.Create("https://rocknrolla.treblle.com");
                httpWebRequest.ContentType = "application/json";
                httpWebRequest.Method = "POST";
                httpWebRequest.Headers.Add("x-api-key", ApiKey);

                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    streamWriter.Write(maskedJson);
                }

                var httpResponse = httpWebRequest.GetResponse();
            }
            catch (Exception ex)
            {
                Logger.LogMessage("An error occured while sending data to Treblle. --- Exception message: " + ex.Message, LogMessageType.Error);
            }
        }
    }
}