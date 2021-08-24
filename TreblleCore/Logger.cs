using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Treblle.Net.Core
{
    public static class Logger
    {
        static string TimestampFormat = "yyyy'-'MM'-'dd' 'HH':'mm':'ss";
        public static void LogMessage(string message, LogMessageType type)
        {
            try
            {
                var now = DateTime.Now;
                string timestamp = now.ToString(TimestampFormat);
                var directory = Environment.CurrentDirectory;
                using (StreamWriter outputFile = new StreamWriter(Path.Combine(directory, "TreblleLog.txt"), append: true))
                {
                    string output = "";
                    switch (type)
                    {
                        case LogMessageType.Error:
                            output = "[ERROR] " + timestamp + " --- " + message;
                            break;
                        case LogMessageType.Info:
                            output = "[INFO] " + timestamp + " --- " + message;
                            break;
                        default:
                            output = timestamp + " --- " + message;
                            break;
                    }

                    outputFile.WriteLine(output);
                }
            }
            catch (Exception e)
            {

            }
        }
    }
}
