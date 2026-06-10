using System;

namespace HelideckVer2.Services.Mru
{
    public struct Vec3
    {
        public double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(double k, Vec3 a) => new Vec3(k * a.X, k * a.Y, k * a.Z);
    }

    public class MruInstallationConfig
    {
        public Vec3 SensorFromCg { get; set; } = new Vec3(0, 0, 0);
        public Vec3 BowFromCg { get; set; } = new Vec3(0, 0, 0);
        public Vec3 SternFromCg { get; set; } = new Vec3(0, 0, 0);
        public Vec3 CustomPointFromCg { get; set; } = new Vec3(0, 0, 0);
        public bool EnablePositionCompensation { get; set; } = true;

        public void SetAll(Vec3 sensorFromCg, Vec3 bowFromCg, Vec3 sternFromCg, Vec3 customPointFromCg, bool enableCompensation = true)
        {
            SensorFromCg = sensorFromCg;
            BowFromCg = bowFromCg;
            SternFromCg = sternFromCg;
            CustomPointFromCg = customPointFromCg;
            EnablePositionCompensation = enableCompensation;
        }
    }

    public class LowPassFilter
    {
        private readonly double _cutoffHz;
        private bool _initialized;
        private double _y;

        public LowPassFilter(double cutoffHz) => _cutoffHz = cutoffHz;

        public double Update(double x, double dt)
        {
            if (!_initialized) { _y = x; _initialized = true; return _y; }
            double rc = 1.0 / (2.0 * Math.PI * _cutoffHz);
            double alpha = dt / (rc + dt);
            _y += alpha * (x - _y);
            return _y;
        }

        public void Reset() { _initialized = false; _y = 0; }
    }

    public class HighPassFilter
    {
        private readonly double _cutoffHz;
        private bool _initialized;
        private double _prevX, _prevY;

        public HighPassFilter(double cutoffHz) => _cutoffHz = cutoffHz;

        public double Update(double x, double dt)
        {
            if (!_initialized) { _prevX = x; _prevY = 0; _initialized = true; return 0; }
            double rc = 1.0 / (2.0 * Math.PI * _cutoffHz);
            double alpha = rc / (rc + dt);
            double y = alpha * (_prevY + x - _prevX);
            _prevX = x; _prevY = y;
            return y;
        }

        public void Reset() { _initialized = false; _prevX = _prevY = 0; }
    }

    public class HeaveCycleAnalyzer
    {
        public double HeaveInstant { get; private set; }
        public double HeaveUp { get; private set; }
        public double HeaveDown { get; private set; }
        public double HeavePeakToPeak { get; private set; }
        public double HeaveAmplitude { get; private set; }
        public double HeavePeriod { get; private set; }
        public double HeaveFrequency { get; private set; }
        public bool CycleValid { get; private set; }
        public string Status { get; private set; } = "INIT";

        public double MinimumPeakToPeak = 0.02;
        public double MinPeriodSeconds = 1.0;
        public double MaxPeriodSeconds = 30.0;

        private bool _initialized;
        private double _time;
        private double _prevHeave;
        private double _prevVelocity;
        private double _currentPeak;
        private double _currentPeakTime;
        private double _currentTrough;
        private double _currentTroughTime;
        private bool _hasPeak;
        private bool _hasTrough;
        private double _lastPeakTime = double.NaN;

        public void Reset()
        {
            HeaveInstant = 0;
            HeaveUp = 0;
            HeaveDown = 0;
            HeavePeakToPeak = 0;
            HeaveAmplitude = 0;
            HeavePeriod = 0;
            HeaveFrequency = 0;
            CycleValid = false;
            Status = "RESET";

            _initialized = false;
            _time = 0;
            _prevHeave = 0;
            _prevVelocity = 0;
            _currentPeak = double.MinValue;
            _currentPeakTime = 0;
            _currentTrough = double.MaxValue;
            _currentTroughTime = 0;
            _hasPeak = false;
            _hasTrough = false;
            _lastPeakTime = double.NaN;
        }

        public void Update(double heave, double dt)
        {
            HeaveInstant = heave;

            if (dt <= 0 || dt > 0.5)
            {
                CycleValid = false;
                Status = "INVALID_DT";
                return;
            }

            _time += dt;

            if (!_initialized)
            {
                _prevHeave = heave;
                _prevVelocity = 0;
                _currentPeak = heave;
                _currentTrough = heave;
                _currentPeakTime = _time;
                _currentTroughTime = _time;
                _initialized = true;
                Status = "WAITING";
                return;
            }

            double velocity = (heave - _prevHeave) / dt;

            if (heave > _currentPeak)
            {
                _currentPeak = heave;
                _currentPeakTime = _time;
            }

            if (heave < _currentTrough)
            {
                _currentTrough = heave;
                _currentTroughTime = _time;
            }

            if (_prevVelocity > 0 && velocity <= 0)
            {
                _hasPeak = true;
                HeaveUp = _currentPeak;

                if (!double.IsNaN(_lastPeakTime))
                {
                    double period = _currentPeakTime - _lastPeakTime;
                    if (period >= MinPeriodSeconds && period <= MaxPeriodSeconds)
                    {
                        HeavePeriod = period;
                        HeaveFrequency = 1.0 / period;
                    }
                }

                _lastPeakTime = _currentPeakTime;
                _currentTrough = heave;
                _currentTroughTime = _time;
            }

            if (_prevVelocity < 0 && velocity >= 0)
            {
                _hasTrough = true;
                HeaveDown = _currentTrough;
                _currentPeak = heave;
                _currentPeakTime = _time;
            }

            if (_hasPeak && _hasTrough)
            {
                double p2p = HeaveUp - HeaveDown;
                if (p2p >= MinimumPeakToPeak)
                {
                    HeavePeakToPeak = p2p;
                    HeaveAmplitude = p2p / 2.0;
                    CycleValid = HeavePeriod >= MinPeriodSeconds && HeavePeriod <= MaxPeriodSeconds;
                    Status = CycleValid ? "CYCLE_VALID" : "WAITING_PERIOD";
                }
                else
                {
                    CycleValid = false;
                    Status = "SMALL_MOTION";
                }
            }

            _prevHeave = heave;
            _prevVelocity = velocity;
        }
    }

    public class MruOutput
    {
        public double RollDeg, PitchDeg, HeadingDeg;
        public double RollRateDegS, PitchRateDegS, YawRateDegS;
        public double VerticalAcceleration, HeaveVelocity;

        public double HeaveInstantCg;
        public double HeaveInstantAtSensor;
        public double HeaveInstantAtBow;
        public double HeaveInstantAtStern;
        public double HeaveInstantAtCustomPoint;

        public double HeaveUpCg;
        public double HeaveDownCg;
        public double HeavePeakToPeakCg;
        public double HeaveAmplitudeCg;
        public double HeavePeriodSeconds;
        public double HeaveFrequencyHz;

        public bool Valid;
        public bool HeaveCycleValid;
        public string Status = "INIT";
    }

    /// <summary>
    /// Kalman filter (2-state: heave + velocity) với ZUPT.
    /// Predict: tích phân acceleration. Update: kéo về 0 khi đứng yên.
    /// </summary>
    public class HeaveKalmanFilter
    {
        private double _h, _v;
        private double _p00 = 0.1, _p01 = 0, _p11 = 0.1;

        // Process noise per second — tăng nếu muốn respond nhanh hơn
        public double QPos = 1e-3;
        public double QVel = 5e-2;

        // ZUPT measurement noise — nhỏ = kéo về 0 nhanh; lớn = kéo chậm
        public double RZupt = 0.1;

        // Ngưỡng gia tốc để kích hoạt ZUPT
        public double AccThreshold = 0.02;
        public double Heave => _h;
        public double Velocity => _v;

        public void Reset() { _h = _v = 0; _p00 = _p11 = 0.1; _p01 = 0; }

        public double Update(double acc, double dt)
        {
            // ── PREDICT: x = F*x + B*a,  P = F*P*F' + Q ──────────────
            double h_p = _h + _v * dt;
            double v_p = _v + acc * dt;

            double p00_p = _p00 + 2 * dt * _p01 + dt * dt * _p11 + QPos * dt;
            double p01_p = _p01 + dt * _p11;
            double p11_p = _p11 + QVel * dt;

            bool stationary = Math.Abs(acc) < AccThreshold;

            if (stationary)
            {
                // ── UPDATE: ZUPT — sequential scalar updates ───────────
                // Heave → 0
                double S0 = p00_p + RZupt;
                double K0h = p00_p / S0;
                double K0v = p01_p / S0;
                h_p += K0h * (-h_p);
                v_p += K0v * (-h_p);
                double u00 = (1 - K0h) * p00_p;
                double u01 = (1 - K0h) * p01_p;
                double u11 = p11_p - K0v * p01_p;

                // Velocity → 0
                double S1 = u11 + RZupt;
                double K1h = u01 / S1;
                double K1v = u11 / S1;
                h_p += K1h * (-v_p);
                v_p += K1v * (-v_p);
                p00_p = u00 - K1h * u01;
                p01_p = (1 - K1v) * u01;
                p11_p = (1 - K1v) * u11;
            }

            _h = h_p;
            _v = v_p;
            _p00 = Math.Max(p00_p, 1e-9);
            _p01 = p01_p;
            _p11 = Math.Max(p11_p, 1e-9);

            return _h;
        }
    }

    public class XsensMruProcessor
    {
        public MruInstallationConfig Installation { get; private set; } = new MruInstallationConfig();

        public double AccDeadband = 0.10;
        public double MaxVerticalAcceleration = 15.0;

        private readonly HeaveKalmanFilter _kalman = new HeaveKalmanFilter();
        private readonly HeaveCycleAnalyzer _cgCycle = new HeaveCycleAnalyzer();


        public void SetSensorPosition(double xMeter, double yMeter, double zMeter)
            => Installation.SensorFromCg = new Vec3(xMeter, yMeter, zMeter);

        public void SetBowPoint(double xMeter, double yMeter, double zMeter)
            => Installation.BowFromCg = new Vec3(xMeter, yMeter, zMeter);

        public void SetSternPoint(double xMeter, double yMeter, double zMeter)
            => Installation.SternFromCg = new Vec3(xMeter, yMeter, zMeter);

        public void SetCustomPoint(double xMeter, double yMeter, double zMeter)
            => Installation.CustomPointFromCg = new Vec3(xMeter, yMeter, zMeter);

        public void EnablePositionCompensation(bool enable)
            => Installation.EnablePositionCompensation = enable;

        public void SetInstallationConfig(MruInstallationConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            Installation = config;
        }

        public void Reset()
        {
            _kalman.Reset();
            _cgCycle.Reset();
        }

        public MruOutput UpdateEuler(
            double rollDeg, double pitchDeg, double yawDeg,
            Vec3 freeAccBody, Vec3 rateOfTurnBody, double dt,
            bool rotInputIsDegPerSec = true)
        {
            MruOutput output = new MruOutput();

            if (dt <= 0.0 || dt > 0.2)
            {
                output.Valid = false;
                output.Status = "INVALID_DT";
                return output;
            }

            double roll = DegToRad(rollDeg);
            double pitch = DegToRad(pitchDeg);
            double yaw = DegToRad(yawDeg);

            Vec3 accEarth = RotateBodyToEarth(freeAccBody, roll, pitch, yaw);
            double verticalAcc = accEarth.Z;  // Z-axis: up = positive

            if (Math.Abs(verticalAcc) > MaxVerticalAcceleration)
            {
                output.Valid = false;
                output.Status = "ACC_SPIKE_REJECTED";
                return output;
            }

            // Kalman Filter with ZUPT (Zero Velocity Update)
            // Predict: integrate acc. Update: pull to 0 when stationary.
            double heaveInstantCg = _kalman.Update(verticalAcc, dt);


            double heaveAtSensor = ApplyPositionCompensation(heaveInstantCg, Installation.SensorFromCg, roll, pitch);
            double heaveAtBow = ApplyPositionCompensation(heaveInstantCg, Installation.BowFromCg, roll, pitch);
            double heaveAtStern = ApplyPositionCompensation(heaveInstantCg, Installation.SternFromCg, roll, pitch);
            double heaveAtCustom = ApplyPositionCompensation(heaveInstantCg, Installation.CustomPointFromCg, roll, pitch);

            _cgCycle.Update(heaveInstantCg, dt);

            Vec3 rotDegS = rotInputIsDegPerSec
                ? rateOfTurnBody
                : new Vec3(RadToDeg(rateOfTurnBody.X), RadToDeg(rateOfTurnBody.Y), RadToDeg(rateOfTurnBody.Z));

            output.RollDeg = rollDeg;
            output.PitchDeg = pitchDeg;
            output.HeadingDeg = Normalize360(yawDeg);
            output.RollRateDegS = rotDegS.X;
            output.PitchRateDegS = rotDegS.Y;
            output.YawRateDegS = rotDegS.Z;

            output.VerticalAcceleration = verticalAcc;
            output.HeaveVelocity = _kalman.Velocity;

            output.HeaveInstantCg = heaveInstantCg;
            output.HeaveInstantAtSensor = heaveAtSensor;
            output.HeaveInstantAtBow = heaveAtBow;
            output.HeaveInstantAtStern = heaveAtStern;
            output.HeaveInstantAtCustomPoint = heaveAtCustom;

            output.HeaveUpCg = _cgCycle.HeaveUp;
            output.HeaveDownCg = _cgCycle.HeaveDown;
            output.HeavePeakToPeakCg = _cgCycle.HeavePeakToPeak;
            output.HeaveAmplitudeCg = _cgCycle.HeaveAmplitude;
            output.HeavePeriodSeconds = _cgCycle.HeavePeriod;
            output.HeaveFrequencyHz = _cgCycle.HeaveFrequency;

            output.HeaveCycleValid = _cgCycle.CycleValid;
            output.Valid = true;
            output.Status = _cgCycle.Status;

            return output;
        }

        private double ApplyPositionCompensation(double heaveCg, Vec3 pointFromCg, double rollRad, double pitchRad)
        {
            if (!Installation.EnablePositionCompensation)
                return heaveCg;
            return GetHeaveAtPoint(heaveCg, pointFromCg, rollRad, pitchRad);
        }

        public static double GetHeaveAtPoint(double heaveCg, Vec3 pointFromCg, double rollRad, double pitchRad)
            => heaveCg + pointFromCg.X * Math.Sin(pitchRad) - pointFromCg.Y * Math.Sin(rollRad);

        public static Vec3 RotateBodyToEarth(Vec3 body, double roll, double pitch, double yaw)
        {
            double cr = Math.Cos(roll), sr = Math.Sin(roll);
            double cp = Math.Cos(pitch), sp = Math.Sin(pitch);
            double cy = Math.Cos(yaw), sy = Math.Sin(yaw);

            return new Vec3(
                (cy * cp) * body.X + (cy * sp * sr - sy * cr) * body.Y + (cy * sp * cr + sy * sr) * body.Z,
                (sy * cp) * body.X + (sy * sp * sr + cy * cr) * body.Y + (sy * sp * cr - cy * sr) * body.Z,
                (-sp) * body.X + (cp * sr) * body.Y + (cp * cr) * body.Z
            );
        }

        public static double DegToRad(double d) => d * Math.PI / 180.0;
        public static double RadToDeg(double r) => r * 180.0 / Math.PI;
        public static double Normalize360(double d) { d %= 360; return d < 0 ? d + 360 : d; }
    }
}
