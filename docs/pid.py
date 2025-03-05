# Test heat simulation with PID controller to maintain a target temperature
# References:
# https://en.wikipedia.org/wiki/PID_controller
# https://github.com/lrklochek/Control_System

from typing import Callable
from itertools import chain
import matplotlib.pyplot as plt
import numpy as np

# Initialize graphs
colors = ["b", "r", "g", "c", "m", "y", "b"]
plt.ion()
fig, (ax, ax_err) = plt.subplots(2)
ln = ax.plot(*chain.from_iterable([[], [], c] for c in colors))
ln_err = ax_err.plot(*chain.from_iterable([[], [], c] for c in colors))
ax.set_xlim(-10, 140)
ax.set_ylim(-2, 22)
ax_err.set_xlim(-10, 140)
ax_err.set_ylim(-1, 1)
plt.show()


def plot(ln, graphs: list[list[tuple[float, float]]]):
    """Plot graphs"""
    for i, points in enumerate(graphs):
        xpoints = np.array([x for x, _ in points])
        ypoints = np.array([y for _, y in points])
        ln[i].set_data(xpoints, ypoints)
    plt.show()


def lerp(a: float, b: float, t: float) -> float:
    """Linearly interpolates between 2 values"""
    return a * (1 - t) + t * b


def clamp(x: float, m: float, M: float) -> float:
    """Clamps a value between a given max and min"""
    return max(m, min(M, x))


# Time step size
dt = 0.01
# Heat transfer constant
heat_transfer = 0.1


def on_off(current: float, target: float) -> float:
    """Trivial controller: heats at max power if target is hotter, and cools at
    max power if target is cooler"""
    return 1 if current <= target else -1


def pid_creator(PROP: float, INTEG: float, DERIV: float):
    """Creates a new PID controller using the given constants. Uses the standard
    implementation"""
    prev_error, cont = (0, 0)
    integral = 0
    prop = []
    deriv = []
    integ = []
    control = []
    errors = []

    def pid(current: float, target: float) -> float:
        """Update the PID controller and return the output"""
        nonlocal prev_error, cont, integral
        # Normalize the inputs and calculate the error
        current /= 50
        target /= 50
        error = target - current
        proportional = error
        errors.append((t, error))
        integral += error * dt
        derivative = (error - prev_error) / dt
        prop.append((t, PROP * proportional))
        integ.append((t, INTEG * integral))
        deriv.append((t, DERIV * derivative))
        cont = PROP * proportional + INTEG * integral + DERIV * derivative
        prev_error = error
        # Clamp the output to [-1, 1]
        cont = clamp(cont, -1, 1)
        control.append((t, cont))
        return clamp(cont, -1, 1)

    return (pid, prop, deriv, integ, control, errors)

def pid_creator2(PROP: float, INTEG: float, DERIV: float):
    """Creates a new PID controller using the given constants. Uses a more complex
    implementation with a derivative term filter and a small state machine to reach
    the target faster after setpoint changes"""
    block_out = 0

    def derivative_filter(time_constant, process_gain, block_in, interval_time):
        """Applies a filter to the derivative term"""
        nonlocal block_out
        block_out += (interval_time / 1000 / (time_constant + interval_time / 1000)) * (
            process_gain * block_in - block_out
        )
        return block_out

    prev_error, cont = (0, 0)
    integral = 0
    prev_target = 0
    margin = 0.5
    reached = True

    def pid(current: float, target: float) -> float:
        """Update the PID controller and return the output"""
        nonlocal prev_error, cont, integral, prev_target, margin, reached
        # Check if the setpoint changed
        if prev_target != target:
            reached = False           
        elif abs(current - target) <= margin:
            reached = True
        prev_target = target
        # Normalize the inputs and calculate the error
        current /= 100
        target /= 100
        error = target - current
        # If the setpoint changed did and we haven't reached the new setpoint
        # yet, use the trivial on-off controller
        if not reached:
            prev_error = error
            return on_off(current, target)
        # Check whether we should disable the integral term to avoid integral
        # windup
        if (cont >= 1 and error > 0) or (cont <= -1 and error < 0) or (integral >= 3600):
            limiter_switch = 0
        else:
            limiter_switch = 1
        integral += 100 / PROP / INTEG * error * dt * limiter_switch
        derivative = 100 / PROP * DERIV * (error - prev_error) / dt
        filtered_derivative = derivative_filter(5, 1, derivative, 1000)
        proportional = 100 / PROP * error
        cont = proportional + integral + filtered_derivative
        prev_error = error
        return clamp(cont, -1, 1)

    return pid


def create_simulation(controller: Callable[[float, float], float]):
    """Creates a new heat simulation with a given controller"""
    current = 0
    ambient_temp = 15
    high_temp = 50
    low_temp = -10
    points: list[tuple[float, float]] = []

    def next():
        """Advances the simulation by 1 step"""
        nonlocal current
        # Get the controller output
        control = controller(current, target)
        # Determine the heat value added according to the controller output
        if control > 0:
            control = lerp(ambient_temp, high_temp, control)
        elif control < 0:
            control = lerp(ambient_temp, low_temp, -control)
        else:
            control = ambient_temp

        points.append((t, current))
        # Update the current temperature according to its difference from the
        # control heat added
        current += heat_transfer * (control - current) * dt

    return (next, points)


# Main loop
while True:
    # Get the PID controller constants from the user
    PROP = float(input("proportional: "))
    if PROP == -1:
        exit(0)
    INTEG = float(input("integral: "))
    if INTEG == -1:
        exit(0)
    DERIV = float(input("derivative: "))
    if DERIV == -1:
        exit(0)

    # Initialize data
    t = 0
    target = 0
    target_points = []
    zero = []

    # Create simulations
    on_off_sim = create_simulation(on_off)
    pid = pid_creator(PROP, INTEG, DERIV)
    pid2 = pid_creator2(PROP, INTEG, DERIV)
    pid_sim = create_simulation(pid[0])
    pid_sim2 = create_simulation(pid2)

    # Simulation loop
    while t < 130:
        target_points.append((t, target))
        zero.append((t, 0))
        # Update simulations
        on_off_sim[0]()
        pid_sim[0]()
        pid_sim2[0]()
        # Update current time
        t += dt
        # Update setpoint
        if t >= 5:
            target = 20
        if t >= 70:
            target = 5

    # Plot results
    plot(ln, [target_points, on_off_sim[1], pid_sim[1], pid_sim2[1]])
    plot(ln_err, [pid[1], pid[2], pid[3], pid[4], pid[5], zero])
