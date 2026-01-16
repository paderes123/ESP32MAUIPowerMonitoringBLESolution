using System.Text.Json.Serialization;

// inside your MainPage class or in a separate file
public class PowerReading
{
    [JsonPropertyName("voltage")]
    public float Voltage { get; set; }

    [JsonPropertyName("current")]
    public float Current { get; set; }

    [JsonPropertyName("frequency")]
    public float Frequency { get; set; }

    [JsonPropertyName("energy")]
    public float Energy { get; set; }

    [JsonPropertyName("powerFactor")]
    public float PowerFactor { get; set; }
}