using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Treblle.Net.Core
{
    public class TreblleOptions
    {
        public TreblleOptions()
        {

        }
        /// <summary>
        /// <param name="exceptionHandlingEnabled">Enable or disable the default Treblle Exception Handler</param>
        /// </summary>
        public TreblleOptions(bool exceptionHandlingEnabled)
        {
            ExceptionHandlingEnabled = exceptionHandlingEnabled;
        }
        /// <summary>
        /// Enable or disable the default Treblle Exception Handler
        /// </summary>
        public bool ExceptionHandlingEnabled { get; set; } = true;
    }
}
