using System;
using System.Collections.Generic;
using System.Text;

namespace ESP32MAUIPowerMonitoringBLE
{
    public class ChartData
    {
        public double Value { get; set; } // x-axis (time)
        public float Size { get; set; }  // y-axis (power)
    }

}
