using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ESP32MAUIPowerMonitoringBLE
{
    public partial class MainPage : ContentPage
    {
        private readonly IBluetoothLE ble = CrossBluetoothLE.Current;
        private readonly IAdapter adapter = CrossBluetoothLE.Current.Adapter;

        private IDevice? connectedDevice;
        private IService? service;
        private ICharacteristic? characteristic;

        private readonly List<Button> connectButtons = new();

        private readonly Guid serviceUuid = Guid.Parse("12345678-1234-1234-1234-1234567890ab");
        private readonly Guid characteristicUuid = Guid.Parse("abcd1234-5678-90ab-cdef-1234567890ab");

        public ObservableCollection<IDevice> DiscoveredDevices { get; } = new();
        public ObservableCollection<ChartData> LiveData { get; } = new();

        // Buffer for incoming BLE data (handles chunks, truncation, out-of-order)
        private readonly StringBuilder bleBuffer = new StringBuilder(2048);

        public MainPage()
        {
            InitializeComponent();
            BindingContext = this;

            Unloaded += (_, _) =>
            {
                adapter.DeviceDiscovered -= OnDeviceDiscovered;
                if (connectedDevice != null)
                {
                    if (characteristic != null)
                        characteristic.ValueUpdated -= OnCharacteristicValueUpdated;
                    _ = adapter.DisconnectDeviceAsync(connectedDevice);
                }
                bleBuffer.Clear();
            };
        }

#pragma warning disable CA1416
        public class BleRuntimePermissions : Permissions.BasePlatformPermission
        {
#if ANDROID
            public override (string androidPermission, bool isRuntime)[] RequiredPermissions => new[]
            {
                (Android.Manifest.Permission.BluetoothScan, true),
                (Android.Manifest.Permission.BluetoothConnect, true)
            };
#endif
        }
#pragma warning restore CA1416

        private async Task<bool> RequestPermissionsAsync()
        {
            var loc = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            var blePerm = await Permissions.RequestAsync<BleRuntimePermissions>();
            return loc == PermissionStatus.Granted && blePerm == PermissionStatus.Granted;
        }

        private async void OnStartScanClicked(object sender, EventArgs e)
        {
            if (!await RequestPermissionsAsync()) return;

            if (ble.State != BluetoothState.On)
            {
                await DisplayAlert("Bluetooth", "Please enable Bluetooth", "OK");
                return;
            }

            DiscoveredDevices.Clear();
            connectButtons.Clear();
            StatusLabel.Text = "Scanning (10s)...";

            adapter.DeviceDiscovered += OnDeviceDiscovered;

            try
            {
                await adapter.StartScanningForDevicesAsync(
                    cancellationToken: new CancellationTokenSource(10000).Token);
                StatusLabel.Text = "Scan complete";
            }
            finally
            {
                adapter.DeviceDiscovered -= OnDeviceDiscovered;
            }
        }

        private void OnDeviceDiscovered(object? sender, DeviceEventArgs e)
        {
            if (e.Device.Name?.Contains("ESP", StringComparison.OrdinalIgnoreCase) != true)
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!DiscoveredDevices.Any(d => d.Id == e.Device.Id))
                    DiscoveredDevices.Add(e.Device);
            });
        }

        private async void OnConnectClicked(object sender, EventArgs e)
        {
            if (sender is not Button btn || btn.CommandParameter is not IDevice device)
                return;

            try
            {
                if (connectedDevice != null)
                {
                    // Disconnect
                    if (characteristic != null)
                        characteristic.ValueUpdated -= OnCharacteristicValueUpdated;

                    await adapter.DisconnectDeviceAsync(connectedDevice);

                    connectedDevice = null;
                    service = null;
                    characteristic = null;
                    bleBuffer.Clear();

                    btn.Text = "Connect";
                    StatusLabel.Text = "Disconnected";
                    return;
                }

                // Connect
                await adapter.ConnectToKnownDeviceAsync(device.Id);
                connectedDevice = device;

                service = await connectedDevice.GetServiceAsync(serviceUuid)
                    ?? throw new Exception("Service not found");

                characteristic = await service.GetCharacteristicAsync(characteristicUuid)
                    ?? throw new Exception("Characteristic not found");

                characteristic.ValueUpdated += OnCharacteristicValueUpdated;
                await characteristic.StartUpdatesAsync();

                // Attempt to negotiate higher MTU (Android only - very helpful)
                try
                {
                    int mtu = await connectedDevice.RequestMtuAsync(185); // returns negotiated value
                    Debug.WriteLine($"Negotiated MTU: {mtu} bytes (usable payload ~{mtu - 3})");

                    if (mtu >= 100)
                        StatusLabel.Text = $"Connected – Good MTU ({mtu})";
                    else
                        StatusLabel.Text = $"Connected – Low MTU ({mtu}) – data may truncate";
                }
                catch (Exception mtuEx)
                {
                    Debug.WriteLine($"MTU request failed: {mtuEx.Message} → using default (~23 bytes)");
                    StatusLabel.Text = "Connected (default MTU ~20 bytes usable)";
                }

                btn.Text = "Disconnect";
                StatusLabel.Text = $"Connected to {device.Name}";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error: {ex.Message}";
                Debug.WriteLine($"Connection failed: {ex}");
            }
        }

        private void OnCharacteristicValueUpdated(object? sender, CharacteristicUpdatedEventArgs e)
        {
            if (e.Characteristic?.Value == null || e.Characteristic.Value.Length == 0)
                return;

            var bytes = e.Characteristic.Value;
            var receivedText = Encoding.UTF8.GetString(bytes);

            var hex = BitConverter.ToString(bytes).Replace("-", " ");
            Debug.WriteLine($"BLE RX | len={bytes.Length,3} | text='{receivedText}'");

            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    // Since full JSON usually arrives → try direct parse first
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var reading = JsonSerializer.Deserialize<PowerReading>(receivedText, options);

                    if (reading != null)
                    {
                        VoltageLabel.Text = reading.Voltage.ToString("F1");
                        CurrentLabel.Text = reading.Current.ToString("F2");
                        FrequencyLabel.Text = reading.Frequency.ToString("F1");
                        EnergyLabel.Text = reading.Energy.ToString("F3");
                        PowerFactorLabel.Text = reading.PowerFactor.ToString("F2");

                        StatusLabel.Text = $"Updated {DateTime.Now:T}";
                        bleBuffer.Clear(); // no need to buffer if parse succeeded
                        return;
                    }

                    // Fallback to buffer logic only if direct parse fails
                    bleBuffer.Append(receivedText);
                    // ... keep your existing while loop for safety ...
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Receive/parse error: {ex.Message}");
                    StatusLabel.Text = "Parse error – check debug output";
                }
            });
        }

        private async void OnToggleSwitchToggled(object sender, ToggledEventArgs e)
        {
            if (characteristic == null) return;

            var cmd = e.Value ? "ON" : "OFF";
            try
            {
                await characteristic.WriteAsync(Encoding.UTF8.GetBytes(cmd));
                StatusLabel.Text = $"Sent: {cmd}";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Write error: {ex.Message}";
            }
        }

        private void OnConnectButtonLoaded(object sender, EventArgs e)
        {
            if (sender is Button b && !connectButtons.Contains(b))
                connectButtons.Add(b);
        }
    }
}