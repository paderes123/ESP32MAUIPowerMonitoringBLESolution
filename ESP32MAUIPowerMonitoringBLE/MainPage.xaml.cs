using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace ESP32MAUIPowerMonitoringBLE
{
    public partial class MainPage : ContentPage
    {
        private readonly IBluetoothLE _ble = CrossBluetoothLE.Current;
        private readonly IAdapter _adapter = CrossBluetoothLE.Current.Adapter;

        private IDevice? _device;
        private IService? _service;
        private ICharacteristic? _characteristic;

        private readonly List<Button> _connectButtons = new();
        private readonly Guid _serviceUuid = Guid.Parse("12345678-1234-1234-1234-1234567890ab");
        private readonly Guid _characteristicUuid = Guid.Parse("abcd1234-5678-90ab-cdef-1234567890ab");

        public ObservableCollection<IDevice> DiscoveredDevices { get; } = new();
        public ObservableCollection<ChartData> LiveData { get; } = new ObservableCollection<ChartData>();


        public MainPage()
        {
            InitializeComponent();
            BindingContext = this;

            Unloaded += (_, _) =>
            {
                _adapter.DeviceDiscovered -= OnDeviceDiscovered;
                if (_device != null)
                    _ = _adapter.DisconnectDeviceAsync(_device);
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
            var ble = await Permissions.RequestAsync<BleRuntimePermissions>();
            return loc == PermissionStatus.Granted && ble == PermissionStatus.Granted;
        }

        private async void OnStartScanClicked(object sender, EventArgs e)
        {
            if (!await RequestPermissionsAsync()) return;
            if (_ble.State != BluetoothState.On)
            {
                await DisplayAlertAsync("Bluetooth", "Enable Bluetooth", "OK");
                return;
            }

            DiscoveredDevices.Clear();
            _connectButtons.Clear();
            StatusLabel.Text = "Scanning (10s)...";

            _adapter.DeviceDiscovered += OnDeviceDiscovered;

            try
            {
                await _adapter.StartScanningForDevicesAsync(
                    cancellationToken: new CancellationTokenSource(10000).Token);
                StatusLabel.Text = "Scan complete";
            }
            finally
            {
                _adapter.DeviceDiscovered -= OnDeviceDiscovered;
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
            if (sender is not Button btn || btn.CommandParameter is not IDevice dev)
                return;

            try
            {
                if (_device != null)
                {
                    if (_characteristic != null)
                        _characteristic.ValueUpdated -= OnValueUpdated;

                    await _adapter.DisconnectDeviceAsync(_device);
                    _device = null;

                    btn.Text = "Connect";
                    StatusLabel.Text = "Disconnected";
                    // disable the dasbord
                    //ToggleSwitch.IsToggled = false;
                    //ToggleSwitch.IsEnabled = false;
                    return;
                }

                await _adapter.ConnectToKnownDeviceAsync(dev.Id);
                _device = dev;

                _service = await _device.GetServiceAsync(_serviceUuid)
                    ?? throw new Exception("Service not found");

                _characteristic = await _service.GetCharacteristicAsync(_characteristicUuid)
                    ?? throw new Exception("Characteristic not found");

                _characteristic.ValueUpdated += OnValueUpdated;
                await _characteristic.StartUpdatesAsync();

                // enable power meter dashboard
                //ToggleSwitch.IsEnabled = true;

                btn.Text = "Disconnect";
                StatusLabel.Text = $"Connected to {dev.Name}";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error: {ex.Message}";
            }
        }

        private void OnValueUpdated(object? sender, CharacteristicUpdatedEventArgs e)
        {
            var text = System.Text.Encoding.UTF8.GetString(e.Characteristic.Value ?? Array.Empty<byte>());

            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    var reading = JsonSerializer.Deserialize<PowerReading>(text);
                    if (reading != null)
                    {
                        VoltageLabel.Text = reading.Voltage.ToString("F1");
                        CurrentLabel.Text = reading.Current.ToString("F2");
                        FrequencyLabel.Text = reading.Frequency.ToString("F1");
                        EnergyLabel.Text = reading.Energy.ToString("F3");
                        PowerFactorLabel.Text = reading.PowerFactor.ToString("F2");

                        // Optional: update chart here if you have LiveData collection
                        // LiveData.Add(new ChartData { Value = DateTime.Now.Second, Size = reading.Power });
                    }

                    StatusLabel.Text = $"Updated at {DateTime.Now:T}";
                }
                catch (JsonException)
                {
                    StatusLabel.Text = $"Invalid JSON: {text}";
                }
            });
        }

        private async void OnToggleSwitchToggled(object sender, ToggledEventArgs e)
        {
            if (_characteristic == null) return;

            var cmd = e.Value ? "ON" : "OFF";
            try
            {
                await _characteristic.WriteAsync(System.Text.Encoding.UTF8.GetBytes(cmd));
                StatusLabel.Text = $"Sent: {cmd}";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Toggle error: {ex.Message}";
            }
        }

        private void OnConnectButtonLoaded(object sender, EventArgs e)
        {
            if (sender is Button b && !_connectButtons.Contains(b))
                _connectButtons.Add(b);
        }
    }
}
