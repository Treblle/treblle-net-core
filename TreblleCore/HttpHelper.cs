using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Treblle.Net.Core
{
    public static class HttpHelper
    {

        public static string ReadBodyAsString(this HttpRequest request)
        {
            var initialBody = request.Body; // Workaround
            var result = string.Empty;
            try
            {
                request.Body.Seek(0, SeekOrigin.Begin);

                using (StreamReader reader = new StreamReader(request.Body))
                {
                    var text = reader.ReadToEndAsync();
                    result = text.Result;
                }


            }
            finally
            {
                // Workaround so MVC action will be able to read body as well
                request.Body = initialBody;
            }

            return result;
        }

        public static async Task<string> ReadBodyAsString(this HttpResponse response)
        {

            response.Body.Seek(0, SeekOrigin.Begin);

            //...and copy it into a string
            string bodyText = await new StreamReader(response.Body).ReadToEndAsync();

            //We need to reset the reader for the response so that the client can read it.
            response.Body.Seek(0, SeekOrigin.Begin);

            return bodyText;
        }
    }
}
