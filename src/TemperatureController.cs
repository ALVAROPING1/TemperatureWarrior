namespace TemperatureWarriorCode
{
    class TemperatureController
    {
        bool isWorking = false;
        long sampleTimeInMilliseconds;
        double setpoint;
        double kp;
        double ki;
        double kd;

        public TemperatureController(long sampleTimeInMilliseconds)
        {
            this.sampleTimeInMilliseconds = sampleTimeInMilliseconds;
            kp = 0;
            ki = 0;
            kd = 0;
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

        public void set_constants(double kp, double ki, double kd)
        {
            this.kp = kp;
            this.ki = ki;
            this.kd = kd;
        }
    }
}
