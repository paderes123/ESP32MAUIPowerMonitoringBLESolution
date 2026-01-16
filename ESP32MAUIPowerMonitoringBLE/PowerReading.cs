using System;
using System.Collections.Generic;
using System.Text;

namespace ESP32MAUIPowerMonitoringBLE
{
    public class PowerReading
    {
        public float Voltage { get; set; }
        public float Current { get; set; }
        public float Power { get; set; }
        public float Energy { get; set; }
        public float Frequency { get; set; }
        public float PowerFactor { get; set; }
    }

}
