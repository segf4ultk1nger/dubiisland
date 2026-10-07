using System;
using System.Collections.Generic;
using System.Text;

namespace ClassIsland.EdgeTts.Model
{
    public enum level
    {
        info,
        warning,
        error
    }
    public class Log
    {
        public string msg { get; set; }
        public level level { get; set; }
    }
}
