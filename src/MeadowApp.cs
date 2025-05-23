// C#
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
// Meadow
using Meadow;
using Meadow.Devices;
using Meadow.Foundation.Sensors.Temperature;
using Meadow.Hardware;
using Meadow.Units;
using NETDuinoWar;
using RingBuffer;
using TemperatureWarriorCode.Web;

public class MeadowApp : App<F7FeatherV2>
{
    const long sensorSampleTime = 100;
    const int oversample = 25;
    double[] median_buffer = new double[oversample];

    double temp_raw;
    double output;
    double p;
    double i;
    double d;

    // long start_ts;

    TemperatureController temperatureController;

    /// Cancelation of the current round
    CancellationTokenSource shutdownCancellationSource = new();

    enum CancellationReason
    {
        ShutdownCommand,
        TempTooHigh,
        ConnectionLost,
    }

    CancellationReason cancellationReason;

    /// Command for the round
    Command? currentCommand;

    /// Update buffers to send during the next client notification
    RingBuffer<double> temp_raw_buf = new();
    RingBuffer<double> output_buf = new();

    RingBuffer<double> p_buf = new();
    RingBuffer<double> i_buf = new();
    RingBuffer<double> d_buf = new();

    const long notification_period = 1000;

    /// System state
    enum OpMode
    {
        /// Waiting for round configuration
        Config,

        /// Waiting for round start
        Prep,

        /// Round execution
        Combat,
    }

    OpMode currentMode = OpMode.Config;

    // private double ts()
    // {
    //     long milliseconds = DateTime.Now.Ticks /
    //     TimeSpan.TicksPerMillisecond; double ts = (double)(milliseconds -
    //     start_ts) / 1000; start_ts = milliseconds; return ts;
    // }

    /// Entry point for the application
    public override async Task Run()
    {
        Resolver.Log.Info("[MeadowApp] ### Init: Run() ###");
        SensorSetup();
        currentMode = OpMode.Config;
        await LaunchNetworkAndWebserver();
        Resolver.Log.Info("[MeadowApp] ### Fin: Run() ###");
    }

    // Get the current temperature from the sensor. Uses the median from
    // several measurements
    private async Task<double> get_temp(AnalogTemperature sensor)
    {
        for (int i = 0; i < oversample; i++)
        {
            median_buffer[i] = (await sensor.Read()).Celsius;
        }
        Array.Sort(median_buffer);
        return median_buffer[oversample / 2];
    }

    /// Configures the sensors
    private void SensorSetup()
    {
        Task.Run(async () =>
        {
            var sensor = new AnalogTemperature(
                analogPin: Device.Pins.A02,
                sensorType: AnalogTemperature.KnownSensorType.TMP36,
                sampleInterval: TimeSpan.FromMilliseconds(0),
                sampleCount: 1
            );

            var sw = new Stopwatch();
            TimeSpan period = TimeSpan.FromMilliseconds(sensorSampleTime);
            while (true)
            {
                // start_ts = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                sw.Restart();
                var measurement = await get_temp(sensor);
                TemperatureUpdateHandler(measurement);
                sw.Stop();
                int rest = (period - sw.Elapsed).Milliseconds;
                if (rest > 0)
                    await Task.Delay(rest);
                // Resolver.Log.Info($"## [round] ## measurement dt = {ts()}s,
                // temp = {measurement}");
            }
        });

        TemperatureController temperatureController = new TemperatureController(
            dt: sensorSampleTime,
            cooler_pwm: Device.CreatePwmPort(
                Device.Pins.D02,
                new Frequency(10, Frequency.UnitType.Hertz),
                0.0f
            ),
            heater_pwm: Device.CreatePwmPort(
                Device.Pins.D04,
                new Frequency(10, Frequency.UnitType.Hertz),
                0.0f
            )
        );
    }

    /// Connects to the WiFi network and launches the web server
    private async Task LaunchNetworkAndWebserver()
    {
        var wifi = Device.NetworkAdapters.Primary<IWiFiNetworkAdapter>();
        if (wifi is null)
        {
            Resolver.Log.Error($"ERROR: No se pudo localizar la interfaz de red primaria");
            return;
        }

        wifi.NetworkConnected += async (networkAdapter, networkConnectionEventArgs) =>
        {
            Resolver.Log.Info($"[MeadowApp] Connected to WiFi -> {networkAdapter.IpAddress}");

            WebSocketServer webServer = new(wifi.IpAddress, Config.Port);
            if (webServer is null)
            {
                Resolver.Log.Error(
                    "[MeadowApp] ERROR: Failed to create a " + "WebSocketServer instance"
                );
                return;
            }
            webServer.MessageReceived += MessageHandler;
            webServer.ConnectionFinished += ConnectionFinishedHandler;
            await webServer.Start();
        };

        Resolver.Log.Info("[MeadowApp] Connecting to WiFi ...");
        while (!wifi.IsConnected)
        {
            try
            {
                await wifi.Connect(Secrets.WIFI_NAME, Secrets.WIFI_PASSWORD);
            }
            catch (NetworkException)
            {
                Resolver.Log.Error(
                    $"ERROR: No se pudo establecer conexión a SSID: {Secrets.WIFI_NAME}\n"
                );
            }
        }
    }

    /// Method to stop the application
    private void Shutdown(CancellationReason reason)
    {
        cancellationReason = reason;
        shutdownCancellationSource.Cancel();
        shutdownCancellationSource = new CancellationTokenSource();
    }

    private void ConnectionFinishedHandler(WebSocketServer webServer, NetworkStream connection)
    {
        Shutdown(CancellationReason.ConnectionLost);
    }

    private void TemperatureTooHighHandler()
    {
        Shutdown(CancellationReason.TempTooHigh);
    }

    /// Method to handle updates on the temperature
    private void TemperatureUpdateHandler(double temp)
    {
        temp_raw = temp;
        if (temp_raw >= Config.MAX_TEMP)
        {
            TemperatureTooHighHandler();
            return;
        }
        output = temperatureController.update(temp_raw, out p, out i, out d);
    }

    /// Handler for messages received by the web server
    private async void MessageHandler(
        WebSocketServer webServer,
        NetworkStream connection,
        Message message
    )
    {
        switch (message.type)
        {
            case Message.MessageType.Command:
                // Can't change command during round
                if (currentMode == OpMode.Combat)
                {
                    Resolver.Log.Error("[MeadowApp] Wait for round end");
                    await webServer.SendMessage(connection, "{\"type\": \"StateError\"}");
                    return;
                }
                if (!message.data.HasValue || !message.data.Value.IsValid())
                {
                    Resolver.Log.Error("[MeadowApp] Invalid command");
                    await webServer.SendMessage(connection, "{\"type\": \"Bad Format\"}");
                    return;
                }

                Resolver.Log.Info("[MeadowApp] Command saved");
                Command cmd = message.data.Value.ToCommand();
                temperatureController.set_constants(cmd.kp, cmd.ki, cmd.kd);
                currentCommand = cmd;
                currentMode = OpMode.Prep;
                await webServer.SendMessage(connection, "{\"type\": \"ConfigOK\"}");
                if (!cmd.isTest)
                {
                    var range = cmd.temperatureRanges.First();
                    var target = getRangeSetpoint(range.MinTemp, range.MaxTemp);
                    temperatureController.SetSetpoint(target);
                    temperatureController.Start();
                }
                break;
            case Message.MessageType.Start:
                if (!currentCommand.HasValue)
                {
                    Resolver.Log.Error("[MeadowApp] Configure command before round start");
                    await webServer.SendMessage(connection, "{\"type\": \"StateError\"}");
                    return;
                }
                if (currentMode == OpMode.Combat)
                {
                    Resolver.Log.Error("[MeadowApp] Can't launch round during another round");
                    await webServer.SendMessage(connection, "{\"type\": \"StateError\"}");
                    return;
                }

                Resolver.Log.Info("[MeadowApp] Starting round");
                currentMode = OpMode.Combat;
                await StartRound(webServer, connection, currentCommand.Value);
                currentCommand = null;
                currentMode = OpMode.Config;
                break;
            case Message.MessageType.Shutdown:
                Resolver.Log.Info("[MeadowApp] Shutdown recibido");
                Shutdown(CancellationReason.ShutdownCommand);
                break;
            default:
                await webServer.SendMessage(connection, "{\"type\": \"Bad Format\"}");
                break;
        }
    }

    private string SerializeNextNotifications(RingBuffer<double> buffer)
    {
        return JsonSerializer.Serialize(
            buffer,
            new JsonSerializerOptions
            {
                Converters = { new RingBufferJsonConverter() },
                WriteIndented = false,
            }
        );
    }

    private async Task NotifyClient(WebSocketServer webServer, NetworkStream connection)
    {
        while (!temp_raw_buf.is_empty() || !output_buf.is_empty())
        {
            await webServer.SendMessage(
                connection,
                $"{{ \"type\": \"N\", \"ns\": [{SerializeNextNotifications(temp_raw_buf)}, {SerializeNextNotifications(output_buf)}]}}"
            );
        }
    }

    private async Task NotifyClientEnd(WebSocketServer webServer, NetworkStream connection)
    {
        while (!p_buf.is_empty() || !i_buf.is_empty() || !d_buf.is_empty())
        {
            await webServer.SendMessage(
                connection,
                $"{{ \"type\": \"Nend\", \"ns\": [{SerializeNextNotifications(p_buf)}, {SerializeNextNotifications(i_buf)}, {SerializeNextNotifications(d_buf)}]}}"
            );
        }
    }

    private void RegisterTimeControllerTemperature(TimeController timeController)
    {
        timeController.RegisterTemperature(temp_raw);
        if (!temp_raw_buf.Enqueue(temp_raw))
            Resolver.Log.Error("[MeadowApp] Fallo en añadir a cola de temp raw");
        if (!output_buf.Enqueue(output))
            Resolver.Log.Error("[MeadowApp] Fallo en añadir a cola de output");
        if (!p_buf.Enqueue(p))
            Resolver.Log.Error("[MeadowApp] Fallo en añadir a cola de output");
        if (!i_buf.Enqueue(i))
            Resolver.Log.Error("[MeadowApp] Fallo en añadir a cola de output");
        if (!d_buf.Enqueue(d))
            Resolver.Log.Error("[MeadowApp] Fallo en añadir a cola de output");
    }

    private static double getRangeSetpoint(double min, double max)
    {
        return min + (max - min) * 0.5;
    }

    private async Task<bool> run_setpoint(double target, int time, CancellationToken cancel_token)
    {
        temperatureController.SetSetpoint(target);
        try
        {
            await Task.Delay(time, cancel_token);
        }
        catch (TaskCanceledException)
        {
            Resolver.Log.Info($"Tarea cancelada: {cancellationReason}");
            return true;
        }
        return false;
    }

    // TW Combat Round
    private async Task StartRound(WebSocketServer webServer, NetworkStream connection, Command cmd)
    {
        Resolver.Log.Info("[MeadowApp] ### Init: StartRound() ###");

        int total_time = cmd.temperatureRanges.Aggregate(
            0,
            (acc, range) => acc + range.RangeTimeInMilliseconds
        );

        TimeController timeController = new() { DEBUG_MODE = false };

        if (
            !timeController.Configure(
                cmd.temperatureRanges,
                total_time,
                cmd.refreshInMilliseconds,
                out string error
            )
        )
        {
            Resolver.Log.Error($"[MeadowApp] Error configurando controlador de tiempo >>> {error}");
            await webServer.SendMessage(connection, "{\"type\": \"TimeControllerConfigError\"}");
            return;
        }

        var cancel_token = shutdownCancellationSource.Token;

        // Resize and reset ringbuffer
        int notifications = (int)
            Math.Ceiling(notification_period / (double)cmd.refreshInMilliseconds);
        var newSize = 2 * notifications;

        temp_raw_buf.ResizeAndReset(newSize);
        output_buf.ResizeAndReset(newSize);

        notifications = (int)Math.Ceiling(total_time / (double)cmd.refreshInMilliseconds);
        newSize = 2 * notifications;
        Resolver.Log.Info($"output size: {newSize}");
        p_buf.ResizeAndReset(newSize);
        i_buf.ResizeAndReset(newSize);
        d_buf.ResizeAndReset(newSize);

        GC.Collect();

        // Launch time controller and register temperature every
        // `refreshInMilliseconds`
        void registerTimeController(object _) => RegisterTimeControllerTemperature(timeController);
        timeController.StartOperation();
        Timer registerTimer = new(registerTimeController, null, 0, cmd.refreshInMilliseconds);
        // Register first temperature
        RegisterTimeControllerTemperature(timeController);

        Timer notificationTimer = new(
            async _ => await NotifyClient(webServer, connection),
            null,
            0,
            notification_period
        );

        // Ranges loop: update setpoint and wait for the next range
        // foreach (var range in cmd.temperatureRanges)
        var ranges = cmd.temperatureRanges;
        for (int i = 0; i < ranges.Length; i++)
        {
            var range = ranges[i];
            var target = getRangeSetpoint(range.MinTemp, range.MaxTemp);
            Resolver.Log.Info($"Iniciando rango [{range.MinTemp} - {range.MaxTemp}]");
            if (i == ranges.Length - 1 || range.MaxTemp - range.MinTemp <= 2)
            {
                if (await run_setpoint(target, range.RangeTimeInMilliseconds, cancel_token))
                    break;
                continue;
            }
            var curr_min = range.MinTemp + 1;
            var curr_max = range.MaxTemp - 1;
            var min = Math.Max(curr_min, ranges[i + 1].MinTemp);
            var max = Math.Min(curr_max, ranges[i + 1].MaxTemp);
            if (max >= min)
                target = getRangeSetpoint(min, max);
            else
            {
                if (ranges[i + 1].MinTemp > range.MaxTemp)
                    target = curr_max;
                else
                    target = curr_min;
            }
            if (await run_setpoint(target, range.RangeTimeInMilliseconds / 2, cancel_token))
                break;
        }

        notificationTimer.Dispose();
        registerTimer.Dispose();

        // Register temperature to not miss the last range
        RegisterTimeControllerTemperature(timeController);

        if (!cmd.isTest)
        {
            temperatureController.Stop();
            Thread.Sleep(100);
        }

        if (cancel_token.IsCancellationRequested)
        {
            switch (cancellationReason)
            {
                case CancellationReason.TempTooHigh:
                    await webServer.SendMessage(
                        connection,
                        $"{{ \"type\": \"TempTooHigh\", \"message\": \"High Temperature Emergency Stop {temp_raw}\" }}"
                    );
                    break;
                case CancellationReason.ShutdownCommand:
                    await webServer.SendMessage(
                        connection,
                        $"{{ \"type\": \"ShutdownCommand\", \"message\": \"Shutdown Command Received\" }}"
                    );
                    break;
                case CancellationReason.ConnectionLost:
                    break;
            }
        }
        else
        {
            // Send round end and final data
            await NotifyClient(webServer, connection);
            await webServer.SendMessage(
                connection,
                $"{{ \"type\": \"RoundFinished\", \"timeInRange\": {timeController.TimeInRangeInMilliseconds}}}"
            );
            await NotifyClientEnd(webServer, connection);
        }

        timeController.FinishOperation();

        Resolver.Log.Info("[MeadowApp] ### Fin: StartRound() ###");
        return;
    }
}

// Ringbuffer serialization
public class RingBufferJsonConverter : JsonConverter<RingBuffer<double>>
{
    public override RingBuffer<double> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        throw new NotImplementedException("Deserialización no implementada");
    }

    public override void Write(
        Utf8JsonWriter writer,
        RingBuffer<double> value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartArray();
        int n = 0;
        while (value.Dequeue(out double item))
        {
            writer.WriteNumberValue(Math.Round(item, 2));
            n++;
            if (n >= 3)
                break;
        }
        writer.WriteEndArray();
    }
}
