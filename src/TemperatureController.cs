using System;
using Meadow.Hardware;

namespace TemperatureWarriorCode
{
    // ╔═══════════════════════════════════════════════════════════════════════╗
    // ║                        Temperature Controller                         ║
    // ╚═══════════════════════════════════════════════════════════════════════╝

    /// Main controller for the system
    class TemperatureController
    {
        /// Whether the controller is actively working
        bool isWorking = false;

        /// Time step
        long dt;

        /// PID controller used to determine how to control the actuators
        PIDController pid;

        /// Actuator for heating the system
        IPwmPort heater;

        /// Actuator for cooling the system
        IPwmPort cooler;

        public TemperatureController(long dt, IPwmPort cooler_pwm, IPwmPort heater_pwm)
        {
            this.dt = dt;
            pid = new PIDController();
            heater = heater_pwm;
            cooler = cooler_pwm;
        }

        /// Start the controller
        public void Start()
        {
            pid.reset();
            heater.Start();
            cooler.Start();
            isWorking = true;
        }

        /// Stop the controller
        public void Stop()
        {
            heater.DutyCycle = 0;
            cooler.DutyCycle = 0;
            heater.Stop();
            cooler.Stop();
            isWorking = false;
        }

        /// Change the target temperature
        public void SetSetpoint(double setpoint)
        {
            pid.SetSetpoint(setpoint);
        }

        /// Change the PID constants
        public void set_constants(double kp, double ki, double kd)
        {
            pid.set_constants(kp, ki, kd);
        }

        /// Function to call during the main update loop
        public double update(double temp, out double p, out double i, out double d)
        {
            p = 0;
            i = 0;
            d = 0;
            if (!isWorking)
                return 0;
            // Update the controller to get the next control output
            double control = pid.update(temp, dt, out p, out i, out d);
            // If it's positive, we must heat up the system
            if (control > 0)
            {
                heater.DutyCycle = control;
                cooler.DutyCycle = 0;
            }
            // If it's negative, we must cool down the system
            else if (control < 0)
            {
                heater.DutyCycle = 0;
                cooler.DutyCycle = -control;
            }
            return control;
        }
    }

    // ╔═══════════════════════════════════════════════════════════════════════╗
    // ║                            PID Controller                             ║
    // ╚═══════════════════════════════════════════════════════════════════════╝

    /// PID controller determining how to control the actuators
    class PIDController
    {
        // ─────────────────────── Controller constants ────────────────────────

        /// Proportional constant
        double kp = 0;

        /// Integral constant
        double ki = 0;

        /// Derivative constant
        double kd = 0;

        /// Target value
        double setpoint = 0;

        /// Filter for the derivative to avoid high-frequency oscillations
        /// during setpoint changes
        LowPassFilter derivative_filter;

        // ────────────────────────── Loop variables ───────────────────────────

        /// Last output generated
        double output = 0;

        /// Error during the previous update call
        double prev_error = 0;

        /// Integral accumulator
        double integral = 0;

        public PIDController()
        {
            derivative_filter = new LowPassFilter(0.1, Config.DERIVATIVE_FILTER_CONSTANT);
        }

        /// Change the PID constants
        public void set_constants(double kp, double ki, double kd)
        {
            this.kp = kp;
            this.ki = ki;
            this.kd = kd;
        }

        /// Change the target value
        public void SetSetpoint(double setpoint)
        {
            this.setpoint = setpoint / 100;
        }

        /// Reset the controller to a stable stand-by state
        public void reset()
        {
            prev_error = 0;
            integral = 0;
            output = 0;
            derivative_filter.reset();
        }

        /// Calculates the next output
        public double update(double current, double dt, out double p, out double i, out double d)
        {
            current = current / 100;
            double error = setpoint - current;
            bool integral_limiter = (output >= 1 && error > 0) || (output <= -1 && error < 0);
            integral += kp / ki * error * dt * (integral_limiter ? 0 : 1);
            integral = Math.Clamp(integral, -1, 1);
            double derivative = kp * kd * (error - prev_error) / dt;
            derivative = derivative_filter.filter(derivative);
            double proportional = kp * error;
            p = proportional;
            i = integral;
            d = derivative;
            output = proportional + integral + derivative;
            output = Math.Max(-1, Math.Min(1, output));
            prev_error = error;
            return output;
        }
    }

    // ╔═══════════════════════════════════════════════════════════════════════╗
    // ║                            Low-Pass Filter                            ║
    // ╚═══════════════════════════════════════════════════════════════════════╝

    /// Filter which removes high-frequency inputs and keeps low frequency ones
    /// intact
    class LowPassFilter
    {
        /// Previous output
        double output = 0;

        /// Filter constant
        double alpha;

        public LowPassFilter(double dt, double rc)
        {
            this.alpha = dt / (rc + dt);
        }

        /// Reset the filter to an initial default value
        public void reset()
        {
            output = 0;
        }

        /// Filter an input value
        public double filter(double input)
        {
            output += alpha * (input - output);
            return output;
        }
    }
}
