namespace TemperatureWarriorCode
{
    class TemperatureController
    {
        bool isWorking = false;
        long sampleTimeInMilliseconds;
        double setpoint;

        public TemperatureController(long sampleTimeInMilliseconds)
        {
            this.sampleTimeInMilliseconds = sampleTimeInMilliseconds;
        }

        public void Start()
        {
            isWorking = true;
        }

        public void Stop()
        {
            isWorking = false;
        }

        public void SetSetpoint(double setpoint)
        {
            this.setpoint = setpoint;
        }

        public double update(double temp)
        {
            if (temp > Config.TemperatureUpperbound.Celsius)
            {
                return 1.0;
            }
            else if (temp < Config.TemperatureLowerbound.Celsius)
            {
                return -1.0;
            }
            return 0.0;
        }
    }
}
