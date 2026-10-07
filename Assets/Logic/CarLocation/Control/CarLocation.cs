using EngineAbstractor;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Assets.Logic.CarLocation
{
    public class CarLocation
    {
        private Location location;

        private const float STEERING_INCREMENT_PER_SECOND = 150;
        private const float STEERING_IDLE = 100;

        public CarPowerTrain PowerTrain { get; private set; }
        public CarChassis Chassis { get; internal set; }

        public bool GasPedal { get; private set; } = false;
        public bool BreakPedal { get; private set; } = false;

        public float DirectionInDegrees { get => location.DirectionInDegrees; }

        public IInputSource InputSource { get; set; }

        public List<string> LastInputs { get; set; }

        public float SteeringAngle => Chassis.SteeringAngle;

        public CarLocation(float wheelBase, Location firstLocation, IInputSource inputSource)
        {
            InputSource = inputSource;

            Chassis = new(wheelBase);
            PowerTrain = new();
            location = firstLocation;
        }

        internal Location NextPosition(float deltaTime)
        {
            var inputs = InputSource.Read();
            float steeringIdleCenter = STEERING_INCREMENT_PER_SECOND / 2000 * deltaTime;

            if (inputs.Contains(InputValue.Left) &&
                !inputs.Contains(InputValue.Right))
                Chassis.SteeringAngle += STEERING_INCREMENT_PER_SECOND * deltaTime;
            else if (!inputs.Contains(InputValue.Left) &&
                     inputs.Contains(InputValue.Right))
                Chassis.SteeringAngle -= STEERING_INCREMENT_PER_SECOND * deltaTime;
            else
            {
                if (Chassis.SteeringAngle < 0)
                    Chassis.SteeringAngle += STEERING_IDLE * deltaTime;
                else if (Chassis.SteeringAngle > 0)
                    Chassis.SteeringAngle -= STEERING_IDLE * deltaTime;
                if (steeringIdleCenter > Math.Abs(Chassis.SteeringAngle))
                    Chassis.SteeringAngle = 0;
            }

            GasPedal = inputs.Contains(InputValue.Foward);
            BreakPedal = inputs.Contains(InputValue.Backward);

            if (GasPedal && !BreakPedal)
                Chassis.Speed = PowerTrain.Accelerate(deltaTime);
            if (!GasPedal && BreakPedal)
                Chassis.Speed = PowerTrain.Break(deltaTime);

            else if (!GasPedal && !BreakPedal)
            {
                if (Chassis.Speed < 0)
                    Chassis.Speed = PowerTrain.Idle(deltaTime);
                else if (Chassis.Speed >= 0)
                    Chassis.Speed = PowerTrain.Idle(deltaTime);
            }

            float displacement = Chassis.Speed * deltaTime;

            if (displacement == 0)
                return null;

            location.DirectionInDegrees += Chassis.RotationAngle(deltaTime);

            location.Point.X += (float)(Math.Cos(location.DirectionInRadians) * displacement);
            location.Point.Y += (float)(Math.Sin(location.DirectionInRadians) * displacement);

            return location;
        }
    }
}
