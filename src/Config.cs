using Meadow.Units;

namespace TemperatureWarriorCode
{
    public static class Config
    {
        /// WEB VARIABLES
        public const int Port = 2550;

        /// PASSWORD COMUNICACIÓN
        public const string PASS = "gpFz#JD&It|BKNDV";

        /// START ROUND VARIABLES
        public static bool isWorking = false;

        /// Temperature constants
        public static readonly double MAX_TEMP = 55.0;
        public static readonly Temperature TemperatureUpperbound = new Temperature(
            30,
            Temperature.UnitType.Celsius
        );
        public static readonly Temperature TemperatureLowerbound = new Temperature(
            12,
            Temperature.UnitType.Celsius
        );

        /// Controller parameters
        public static readonly double SENSOR_FILTER_CONSTANT = 0.65;
        public static readonly double DERIVATIVE_FILTER_CONSTANT = 0.1;
    }
}
