// C#
using System;
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

namespace TemperatureWarriorCode
{
    public class MeadowApp : App<F7FeatherV2>
    {
        /// Sensor de temperatura
        AnalogTemperature sensor;
        TimeSpan sensorSampleTime = TimeSpan.FromSeconds(0.1);

        Temperature currentTemperature;

        TemperatureController temperatureController;
        bool temperatureHandlerRunning = false; // Evitar overlapping de handlers

        // Estado del actuador en un rango de temperatura

        double currentSetpoint;
        TemperatureRange currentRange;

        /// Cancelación de ronda en curso
        CancellationTokenSource shutdownCancellationSource = new();

        enum CancellationReason
        {
            ShutdownCommand,
            TempTooHigh,
            ConnectionLost,
        }

        CancellationReason cancellationReason;

        /// Estado inter-comando para la librería de registro de temperatura
        int totalOperationTimeInMilliseconds = 0;
        int totalTimeInRangeInMilliseconds = 0;
        int totalTimeOutOfRangeInMilliseconds = 0;

        /// El comando a ejecutar
        Command? currentCommand;

        /// Buffer de actualizaciones a enviar en la próxima notifiación al cliente
        RingBuffer<double> nextNotificationsBuffer = new();
        readonly long notificationPeriodInMilliseconds = 2000;

        /// El modo de ejecución del sistema
        enum OpMode
        {
            /// Parámetros de ronda no configurados
            Config,

            /// Parámetros de ronda configurados, esperando comando de inicio de
            /// combate
            Prep,

            /// Ejecutando ronda
            Combat,
        }

        OpMode currentMode = OpMode.Config;

        /// Entry point for the application
        public override async Task Run()
        {
            Resolver.Log.Info("[MeadowApp] ### Init: Run() ###");

            // Configurar sensores
            SensorSetup();

            // Configurar modo inicial ('esperando configuración')
            currentMode = OpMode.Config;

            await LaunchNetworkAndWebserver();

            Resolver.Log.Info("[MeadowApp] ### Fin: Run() ###");
            return;
        }

        /// Configures the sensors
        private void SensorSetup()
        {
            // TODO Inicializar sensores de actuadores

            temperatureController = new TemperatureController(
                sampleTimeInMilliseconds: sensorSampleTime.Milliseconds
            );

            // Configuración de Sensor de Temperatura
            sensor = new AnalogTemperature(
                analogPin: Device.Pins.A02,
                sensorType: AnalogTemperature.KnownSensorType.TMP36
            );

            sensor.Updated += TemperatureUpdateHandler;
            sensor.StartUpdating(sensorSampleTime);
        }

        /// Connects to the WiFi network and launches the web server
        private async Task LaunchNetworkAndWebserver()
        {
            // Configuración de Red
            var wifi = Device.NetworkAdapters.Primary<IWiFiNetworkAdapter>();
            if (wifi is null)
            {
                Resolver.Log.Error($"ERROR: No se pudo localizar la interfaz de red primaria");
                return;
            }

            Resolver.Log.Info("[MeadowApp] Connecting to WiFi ...");
            try
            {
                await wifi.Connect(Secrets.WIFI_NAME, Secrets.WIFI_PASSWORD);
            }
            catch (NetworkException e)
            {
                Resolver.Log.Error(
                    $"ERROR: No se pudo establecer conexión a SSID: {Secrets.WIFI_NAME}\n"
                        + $"    - Error cause: {e}"
                );
                return;
            }
            if (!wifi.IsConnected)
            {
                Resolver.Log.Error(
                    $"ERROR: No se pudo establecer conexión a SSID: {Secrets.WIFI_NAME}"
                );
                return;
            }

            wifi.NetworkConnected += async (networkAdapter, networkConnectionEventArgs) =>
            {
                Resolver.Log.Info($"[MeadowApp] Connected to WiFi -> {networkAdapter.IpAddress}");

                // Lanzar Servidor de Comandos
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
        private void TemperatureUpdateHandler(object sender, IChangeResult<Temperature> e)
        {
            currentTemperature = e.New;
            // Resolver.Log.Info($"[MeadowApp] DEBUG (Remove this console line):
            // Current temperature={currentTemperature}");

            if (temperatureHandlerRunning)
                return;
            temperatureHandlerRunning = true;

            double temp = currentTemperature.Celsius;
            if (temp >= 55.0)
            {
                TemperatureTooHighHandler();
                return;
            }
            double output = temperatureController.update(currentTemperature.Celsius);

            temperatureHandlerRunning = false;
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
                    // No se puede cambiar el comando a la mitad de una ronda en curso
                    if (currentMode == OpMode.Combat)
                    {
                        Resolver.Log.Error(
                            "[MeadowApp] Esperar a la finalización de " + "la ronda en curso"
                        );
                        await webServer.SendMessage(connection, "{\"type\": \"StateError\"}");
                        return;
                    }
                    if (!message.data.HasValue || !message.data.Value.IsValid())
                    {
                        Resolver.Log.Error("[MeadowApp] Comando inválido");
                        await webServer.SendMessage(connection, "{\"type\": \"Bad Format\"}");
                        return;
                    }

                    Resolver.Log.Info("[MeadowApp] Comando guardado");
                    Command cmd = message.data.Value.ToCommand();
                    temperatureController.set_constants(cmd.kp, cmd.ki, cmd.kd);
                    currentCommand = cmd;
                    currentMode = OpMode.Prep;
                    await webServer.SendMessage(connection, "{\"type\": \"ConfigOK\"}");
                    break;
                case Message.MessageType.Start:
                    if (!currentCommand.HasValue)
                    {
                        Resolver.Log.Error(
                            "[MeadowApp] Configurar comando antes de " + "iniciar ejecución"
                        );
                        await webServer.SendMessage(connection, "{\"type\": \"StateError\"}");
                        return;
                    }
                    if (currentMode == OpMode.Combat)
                    {
                        Resolver.Log.Error(
                            "[MeadowApp] No se puede lanzar una ronda "
                                + "mientras otra está en curso"
                        );
                        await webServer.SendMessage(connection, "{\"type\": \"StateError\"}");
                        return;
                    }

                    Resolver.Log.Info("[MeadowApp] Iniciando ejecución de comando");
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

        private string SerializeNextNotifications()
        {
            return JsonSerializer.Serialize(
                nextNotificationsBuffer,
                new JsonSerializerOptions
                {
                    Converters = { new RingBufferJsonConverter() },
                    WriteIndented = false,
                }
            );
        }

        private Task NotifyClient(WebSocketServer webServer, NetworkStream connection)
        {
            return webServer.SendMessage(
                connection,
                $"{{ \"type\": \"N\", \"ns\": {SerializeNextNotifications()}}}"
            );
        }

        private void RegisterTimeControllerTemperature(TimeController timeController)
        {
            var currTemp = currentTemperature.Celsius;
            timeController.RegisterTemperature(currTemp);
            if (!nextNotificationsBuffer.Enqueue(currTemp))
                Resolver.Log.Error("[MeadowApp] Fallo en añadir a cola de notifiaciones");
        }

        // TW Combat Round
        private async Task StartRound(
            WebSocketServer webServer,
            NetworkStream connection,
            Command cmd
        )
        {
            Resolver.Log.Info("[MeadowApp] ### Init: StartRound() ###");

            int total_time = cmd.temperatureRanges.Aggregate(
                0,
                (acc, range) => acc + range.RangeTimeInMilliseconds
            );

            // Inicialización de librería de control
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
                Resolver.Log.Error(
                    $"[MeadowApp] Error configurando controlador de tiempo >>> {error}"
                );
                await webServer.SendMessage(
                    connection,
                    "{\"type\": \"TimeControllerConfigError\"}"
                );
                return;
            }

            var shutdownCancellationToken = shutdownCancellationSource.Token;

            double getRangeSetpoint(TemperatureRange range) =>
                range.MinTemp + (range.MaxTemp - range.MinTemp) * 0.5;

            if (!cmd.isTest)
            {
                temperatureController.SetSetpoint(getRangeSetpoint(cmd.temperatureRanges.First()));
                temperatureController.Start();
            }

            //// Acomodar tamaño de ringbuffer y zero-out ringbuffer
            // Debemos ser capaces de ingresar ceil(notificationPeriodInMilliseconds
            // / cmd.refreshInMilliseconds), además multiplicamos este resultado por
            // 2 para dar algo de "wiggle room".
            int notifications = (int)
                Math.Ceiling(notificationPeriodInMilliseconds / (double)cmd.refreshInMilliseconds);
            var newSize = 2 * notifications;

            nextNotificationsBuffer.ResizeAndReset(newSize);

            //// Lanzar conteo en librería de control cada refreshInMilliseconds
            void registerTimeController(object _) =>
                RegisterTimeControllerTemperature(timeController);
            timeController.StartOperation();
            Timer registerTimer = new(registerTimeController, null, 0, cmd.refreshInMilliseconds);

            // Enviar primera temperatura medida
            RegisterTimeControllerTemperature(timeController);

            //// Notificaciones al cliente
            Timer notificationTimer = new(
                async _ => await NotifyClient(webServer, connection),
                null,
                0,
                notificationPeriodInMilliseconds
            );

            foreach (var range in cmd.temperatureRanges)
            { // modificar setpoint en
                // cada iteración
                currentSetpoint = getRangeSetpoint(range);
                currentRange = range;
                temperatureController.SetSetpoint(currentSetpoint);
                Resolver.Log.Info($"Iniciando rango [{range.MinTemp} - {range.MaxTemp}]");
                try
                {
                    await Task.Delay(range.RangeTimeInMilliseconds, shutdownCancellationToken);
                }
                catch (TaskCanceledException)
                {
                    // En caso de cancelación por temperature alta, escapar de loop
                    // (notificación a cliente se maneja abajo)
                    break;
                }
            }

            // Apagar actuadores y desactivar timers/librería de registro de temp
            notificationTimer.Dispose();
            registerTimer.Dispose();

            // Cuando el tiempo de operación de la ronda es divisible por el tiempo
            // de refresco, se pierde la última medición
            if (total_time / cmd.refreshInMilliseconds == 0)
                RegisterTimeControllerTemperature(timeController);

            // Send remaining data
            await NotifyClient(webServer, connection);

            if (!cmd.isTest)
            { // Apagar actuador en caso de no ser un test de
                // sensor de temperatura
                temperatureController.Stop();
                Thread.Sleep(100);
            }

            if (shutdownCancellationToken.IsCancellationRequested)
            { // Notificar finalización por altas
                // temperaturas
                switch (cancellationReason)
                {
                    case CancellationReason.TempTooHigh:
                        await webServer.SendMessage(
                            connection,
                            $"{{ \"type\": \"TempTooHigh\", \"message\": \"High Temperature Emergency Stop {currentTemperature}\" }}"
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
            { // Calcular resultados
                if (!cmd.isTest)
                {
                    // Solamente actualizar estado inter-ronda si no se trata de un
                    // test de sensores
                    totalTimeInRangeInMilliseconds += timeController.TimeInRangeInMilliseconds;
                    totalTimeOutOfRangeInMilliseconds +=
                        timeController.TimeOutOfRangeInMilliseconds;
                    totalOperationTimeInMilliseconds += total_time;
                    Resolver.Log.Info(
                        $"Global - Tiempo dentro del rango {totalTimeInRangeInMilliseconds} ms de {totalOperationTimeInMilliseconds}s"
                    );
                    Resolver.Log.Info(
                        $"Global - Tiempo fuera del rango {totalTimeOutOfRangeInMilliseconds} ms de {totalOperationTimeInMilliseconds}s"
                    );
                }

                Resolver.Log.Info(
                    $"Ronda - Tiempo dentro del rango {timeController.TimeInRangeInMilliseconds} ms de {total_time} ms"
                );
                Resolver.Log.Info(
                    $"Ronda - Tiempo fuera del rango {timeController.TimeOutOfRangeInMilliseconds} ms de {total_time} ms"
                );

                // Indicar finalización y enviar datos de refresco restantes en el
                // buffer
                await webServer.SendMessage(
                    connection,
                    $"{{ \"type\": \"RoundFinished\", \"timeInRange\": {timeController.TimeInRangeInMilliseconds}, \"ns\": {SerializeNextNotifications()}}}"
                );
            }

            timeController.FinishOperation();

            Resolver.Log.Info("[MeadowApp] ### Fin: StartRound() ###");
            return;
        }
    }

    // Serialización de ringbuffer de notificaciones
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
            while (value.Dequeue(out double item))
            {
                writer.WriteNumberValue(Math.Round(item, 2));
            }
            writer.WriteEndArray();
        }
    }
}
