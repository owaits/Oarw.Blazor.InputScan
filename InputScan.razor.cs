using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Blazor.Bluetooth;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Oarw.Blazor.InputScan
{
    public partial class InputScan : IDisposable
    {
        public enum Variants
        {
            Dropdown,
            RadioButtons
        }

        private Audio? successSound;
        private Audio? failSound;
        private Audio? addSound;
        private Audio? excessSound;
        private Audio? completeSound;
        private ElementReference scanInput;
        private ElementReference scanMethod;

        [Inject]
        public IJSRuntime? JS { get; set; }

        [Inject]
        public IServiceProvider? ServiceProvider { get; set; }

        [Inject]
        public ILogger<InputScan> Log { get; set; }

        protected IBluetoothNavigator? Bluetooth { get; set; }

        private string title = null;

        [Parameter]
        public string Title
        {
            get { return title; }
            set
            {
                if (title != value)
                {
                    title = value;
                    StateHasChanged();
                }
            }
        }

        [Parameter]
        public int MaxScanHistory { get; set; } = 5;

        [Parameter]
        public bool BluetoothEnabled { get; set; } = true;

        /// <summary>
        /// Gets or sets whether a keypad is displayed to allow the barcode to be entered manually with the keypad.
        /// </summary>
        [Parameter]
        public bool KeypadEnabled { get; set; } = false;

        [Parameter]
        public bool KeypadVisible { get; set; } = false;


        [Parameter]
        public bool CameraEnabled { get; set; } = false;

        public bool CameraVisible { get; set; } = false;

        /// <summary>
        /// Gets or sets the text entered when optional key A is pressed.
        /// </summary>
        [Parameter]
        public string UserKeyA { get; set; } = "A";

        /// <summary>
        /// Gets or sets the text entered when optional key B is pressed.
        /// </summary>
        [Parameter]
        public string UserKeyB { get; set; } = "B";

        [Parameter]
        public RenderFragment<object> Result { get; set; }

        [Parameter]
        public Variants Variant { get; set; } = Variants.Dropdown;

        [Parameter]
        public RenderFragment ChildContent { get; set; }

        protected override async Task OnInitializedAsync()
        {
            Bluetooth = ServiceProvider?.GetService<IBluetoothNavigator>();

            if (Bluetooth == null)
            {
                BluetoothEnabled = false;
            }

            if (BluetoothEnabled)
            {
                try
                {
                    await ConnectBluetoothScanner(false);
                }
                catch (Exception ex)
                {
                    Log.LogError(ex, "Unable to reconnect to bluetooth scanner.");
                }
            }

            await Task.CompletedTask;
        }

        private string scanValue = string.Empty;

        public string ScanValue
        {
            get { return scanValue; }
            set
            {
                var barcodeValue = value;
                if (scanValue != barcodeValue)
                {
                    scanValue = barcodeValue;
                    code = scanValue;

                    if (!string.IsNullOrEmpty(scanValue))
                    {
                        InvokeAsync(async () =>
                        {
                            object result;

                            try
                            {
                                result = await SelectedInstruction?.OnScan(scanValue);
                            }
                            catch (Exception ex)
                            {
                                result = $"ERROR: {ex.Message}";
                            }                            

                            //If this instruction is set to single scan mode then reset the instruction back to the default.
                            if (SelectedInstruction.SingleScan)
                                SelectedInstruction = DefaultInstruction;

                            scanValue = string.Empty;

                            if(result != null)
                            {
                                ScanLog.Enqueue(result);

                                //Trim the scan history to the max items.
                                while (ScanLog.Count > MaxScanHistory)
                                    ScanLog.Dequeue();

                                StateHasChanged();
                            
                                if (result.ToString()?.StartsWith("OK:") ?? false)
                                    await (successSound?.Play() ?? Task.CompletedTask);
                                else if (result.ToString()?.StartsWith("ADD:") ?? false)
                                    await (addSound?.Play() ?? Task.CompletedTask);
                                else if (result.ToString()?.StartsWith("EXCESS:") ?? false)
                                    await (excessSound?.Play() ?? Task.CompletedTask);
                                else if (result.ToString()?.StartsWith("COMPLETE:") ?? false)
                                    await (completeSound?.Play() ?? Task.CompletedTask);
                                else
                                    await (failSound?.Play() ?? Task.CompletedTask);
                            }

                        });

                    }
                }
            }
        }

        /// <summary>
        /// Attempt to prevent enter presses submitting the form.
        /// </summary>
        /// <param name="e"></param>
        private async void HandleKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
            {
                //Toggle the focus so that the scanner input is applied but the key press events are blocked from propogating up to the form.
                //This ensures that the form is not submitted by the scanner alone.
                await scanMethod.FocusAsync();
                await scanInput.FocusAsync();
            }
        }

        #region Scan Instructions

        private HashSet<ScanInstruction> Instructions { get; set; } = new HashSet<ScanInstruction>();

        protected ScanInstruction? SelectedInstruction { get; set; }

        protected ScanInstruction? DefaultInstruction { get; set; }


        public void AddInstruction(ScanInstruction instruction)
        {
            Instructions.Add(instruction);

            //Setup the default instruction to use.
            if(instruction.Default || DefaultInstruction == null)
            {
                DefaultInstruction = instruction;
            }

            //Initialise the selected instruction to either the default or the first instruction we come across.
            if(instruction.Default || SelectedInstruction == null)
            {
                SelectInstruction(instruction);
            }
        }

        public void SelectInstruction(ScanInstruction instruction)
        {
            SelectedInstruction = instruction;

            if (instruction.ClearLog)
                ScanLog.Clear();

            JS.InvokeAsync<string>("setFocus", "scanInput");
        }

        #endregion

        [Parameter]
        public string code { get; set; }

        /// <summary>
        /// Gets or sets whether the scan input is pinned to the top of the page.
        /// </summary>
        [Parameter]
        public bool PinTop { get; set; }


        public Queue<object> ScanLog { get; set; } = new Queue<object>(5);

        private Task OnCameraError(string message)
        {
            ScanLog.Enqueue(message);
            StateHasChanged();
            return Task.CompletedTask;
        }

        private string GetScanLogLevel(object scanResult)
        {
            string resultMessage = scanResult.ToString();
            if (resultMessage.StartsWith("OK:") || resultMessage.StartsWith("COMPLETE:"))
                return "success";

            if (resultMessage.StartsWith("ADD:") || resultMessage.StartsWith("EXCESS:"))
                return "warning";

            return "danger";
        }

        #region Bluetooth Scanner

        private List<Filter> bluetoothScannerSupportedDevices = new List<Filter>()
        {
            //Inatek BCST-42
            new Filter()
            {
                NamePrefix = "HPRT-"
            }                    
        };

        private string[] bluetoothScannerServiceIds =
        {
            "0000feea-0000-1000-8000-00805f9b34fb"          //Inatek BCST-42
        };

        private string scannerBluetoothReadBarcodeCharacteristic = "00002aa1-0000-1000-8000-00805f9b34fb";

        private string bluetoothScanBuffer;

        private Timer bluetoothConnectionTimer;

        private IDevice? connectedBluetoothDevice = null;

        /// <summary>
        /// Gets or sets the connected bluetooth device.
        /// </summary>
        public IDevice? ConnectedBluetoothDevice 
        {
            get { return connectedBluetoothDevice; } 
            set
            {
                connectedBluetoothDevice = value;

                if (OnBluetoothDeviceConnected != null)
                {
                    InvokeAsync(async ()=> await OnBluetoothDeviceConnected(connectedBluetoothDevice));
                }                
            }
        }

        /// <summary>
        /// Called when a Bluetooth device is connected.
        /// </summary>
        [Parameter]
        public Func<IDevice?, Task>? OnBluetoothDeviceConnected { get; set; }

        private bool isBluetoothScannerConnected = false;

        public bool IsBluetoothScannerConnected 
        {
            get { return isBluetoothScannerConnected; }
            set
            {
                if(isBluetoothScannerConnected != value)
                {
                    isBluetoothScannerConnected = value;
                    InvokeAsync(async () => StateHasChanged());
                }
            }
        }

        private bool isBluetoothScannerConnecting = false;

        public bool IsBluetoothScannerConnecting
        {
            get { return isBluetoothScannerConnecting; }
            set
            {
                if (isBluetoothScannerConnecting != value)
                {
                    isBluetoothScannerConnecting = value;
                    InvokeAsync(async () => StateHasChanged());
                }
            }
        }

        protected async Task ConnectBluetoothScanner(bool connectNewDevice)
        {
            if (bluetoothConnectionTimer != null)
            {
                bluetoothConnectionTimer.Dispose();
                bluetoothConnectionTimer = null;
            }

            var query = new RequestDeviceQuery
            {
                Filters = bluetoothScannerSupportedDevices,
                OptionalServices = bluetoothScannerServiceIds.ToList()
            };

            try
            {
                IDevice? device = null;
                if(Bluetooth != null)
                {
                    IsBluetoothScannerConnecting = true;

                    if (connectNewDevice)
                    {
                        device = await Bluetooth.RequestDevice(query);
                    }
                    else
                    {
                        device = (await Bluetooth.GetDevices())?.FirstOrDefault();
                    }
                }

                if (device == null)
                    return;

                await BeginConnectToBluetoothScanner(device);
            }
            catch(RequestDeviceCancelledException ex)
            {
                //User cancelled the Bluetooth request.
                Log.LogInformation(ex, "User cancelled Bluetooth pairing.");
            }
            catch (Exception ex)
            {
                //User cancelled the Bluetooth request.
                Log.LogError(ex, "Unable to connect to Bluetooth device.");
            }
            finally
            {
                IsBluetoothScannerConnecting = false;
            }
            
        }

        private async Task BeginConnectToBluetoothScanner(object state)
        {
            IDevice? device = (IDevice?)state;
            await InvokeAsync(async () =>
            {
                try
                {
                    await device.Gatt.Connect();

                    var service = await device.Gatt.GetPrimaryService(bluetoothScannerServiceIds[0]);
                    var characteristic = await service.GetCharacteristic(scannerBluetoothReadBarcodeCharacteristic);
                    characteristic.OnRaiseCharacteristicValueChanged += (sender, e) =>
                    {
                        bluetoothScanBuffer += Encoding.UTF8.GetString(e.Value);

                        if (bluetoothScanBuffer.EndsWith("\r"))
                        {
                            ScanValue = bluetoothScanBuffer;
                            bluetoothScanBuffer = string.Empty;
                        }
                    };
                    await characteristic.StartNotifications();

                    device.OnGattServerDisconnected += async () =>
                    {
                        IsBluetoothScannerConnected = false;
                        ConnectedBluetoothDevice = null;

                        await BeginConnectToBluetoothScanner(device);
                    };

                    StateHasChanged();
                }
                catch (Exception ex)
                {
                    Log.LogWarning(ex, "Attempt to connect to bluetooth scanner failed.");
                }
            });

            if (!device.Gatt.Connected)
            {
                if (bluetoothConnectionTimer == null)
                {
                    bluetoothConnectionTimer = new Timer(new TimerCallback((state)=> BeginConnectToBluetoothScanner(device)), device, TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan);
                }
                else
                {
                    bluetoothConnectionTimer.Change(TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan);
                }
            }


            ConnectedBluetoothDevice = device;
            IsBluetoothScannerConnected = device.Gatt.Connected;            
        }

        #endregion

        #region Keypad

        /// <summary>
        /// Determines the text value for a given key position in the keypad.
        /// </summary>
        /// <param name="keyIndex">Index of the key.</param>
        /// <returns>The key text that is displayed and entered.</returns>
        private string GetKeyAtIndex(int keyIndex)
        {
            switch (keyIndex)
            {
                case 10:
                    return UserKeyA;
                case 11:
                    return "0";
                case 12:
                    return UserKeyB;
            }

            return keyIndex.ToString();
        }

        /// <summary>
        /// Called when a key is pressed on the keypad.
        /// </summary>
        /// <param name="key">The key that was pressed.</param>
        public void KeyPress(string key)
        {
            if(key == Environment.NewLine)
            {
                ScanValue = scanValue + key;
            }
            else
            {
                scanValue += key;
                StateHasChanged();
            }            
        }

        /// <summary>
        /// Performs a backspace on the keypad.
        /// </summary>
        public void KeypadBackspace()
        {
            if(scanValue.Length > 0)
            {
                scanValue = scanValue.Substring(0,scanValue.Length-1);
            }           
        }

        public void ToggleKeypad()
        {
            KeypadVisible = !KeypadVisible;
            StateHasChanged();
        }

        #endregion

        #region Camera

        public async Task ToggleCamera()
        {
            CameraVisible = !CameraVisible;
            StateHasChanged();
        }

        /// <summary>
        /// Called when the camera sees a QR code.
        /// </summary>
        /// <param name="code">The code.</param>
        private void OnCameraScan(string code)
        {
            //Set the scan value using the camera input.
            ScanValue = code;
        }

        #endregion

        public void Dispose()
        {
            if(bluetoothConnectionTimer != null)
            {
                bluetoothConnectionTimer.Dispose();
                bluetoothConnectionTimer = null;
            }

            if(ConnectedBluetoothDevice != null)
            {
                ConnectedBluetoothDevice.Gatt.Disonnect();
                ConnectedBluetoothDevice = null;
            }
        }
    }
}
