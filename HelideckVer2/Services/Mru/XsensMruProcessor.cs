using System;

namespace HelideckVer2.Services.Mru
{
    public struct Vec3
    {
        public double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X+b.X, a.Y+b.Y, a.Z+b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X-b.X, a.Y-b.Y, a.Z-b.Z);
        public static Vec3 operator *(double k, Vec3 a) => new Vec3(k*a.X, k*a.Y, k*a.Z);
    }

    public class LowPassFilter
    {
        private readonly double _cutoffHz;
        private bool   _init;
        private double _y;
        public LowPassFilter(double cutoffHz) { _cutoffHz = cutoffHz; }

        public double Update(double x, double dt)
        {
            if (!_init) { _y = x; _init = true; return _y; }
            double rc    = 1.0 / (2.0 * Math.PI * _cutoffHz);
            double alpha = dt / (rc + dt);
            _y += alpha * (x - _y);
            return _y;
        }
        public void Reset(double value = 0) { _y = value; _init = false; }
    }

    public class HighPassFilter
    {
        private readonly double _cutoffHz;
        private bool   _init;
        private double _prevX, _prevY;
        public HighPassFilter(double cutoffHz) { _cutoffHz = cutoffHz; }

        public double Update(double x, double dt)
        {
            if (!_init) { _prevX = x; _prevY = 0; _init = true; return 0; }
            double rc    = 1.0 / (2.0 * Math.PI * _cutoffHz);
            double alpha = rc / (rc + dt);
            double y     = alpha * (_prevY + x - _prevX);
            _prevX = x; _prevY = y;
            return y;
        }
        public void Reset() { _init = false; _prevX = _prevY = 0; }
    }

    public class MruOutput
    {
        public double RollDeg, PitchDeg, HeadingDeg;
        public double RollRateDegS, PitchRateDegS, YawRateDegS;
        public double VerticalAcceleration, HeaveVelocity;
        public double HeaveCg;           // m, tại CG tàu
        public double HeaveAtSensor;
        public double HeaveAtBow;
        public double HeaveAtStern;
        public double HeaveAtCustomPoint;
        public bool   Valid;
        public string Status = "INIT";
    }

    public class XsensMruProcessor
    {
        // Vị trí lắp đặt (mét, theo hệ tàu: X=mũi+, Y=mạn phải+, Z=lên+)
        public Vec3 SensorPositionFromCg = new Vec3(0, 0, 0);
        public Vec3 BowPointFromCg       = new Vec3(0, 0, 0);
        public Vec3 SternPointFromCg     = new Vec3(0, 0, 0);
        public Vec3 CustomPointFromCg    = new Vec3(0, 0, 0);

        // Bộ lọc
        public double AccDeadband            = 0.015;  // m/s²
        public double MaxVerticalAcceleration = 15.0;  // m/s²

        private readonly LowPassFilter  _accLowPass   = new LowPassFilter(2.0);
        private readonly HighPassFilter _velHighPass  = new HighPassFilter(0.05);
        private readonly HighPassFilter _heaveHighPass = new HighPassFilter(0.03);

        private double _heaveVelocityRaw;
        private double _heaveRaw;

        public void Reset()
        {
            _heaveVelocityRaw = _heaveRaw = 0;
            _accLowPass.Reset();
            _velHighPass.Reset();
            _heaveHighPass.Reset();
        }

        public MruOutput UpdateEuler(
            double rollDeg, double pitchDeg, double yawDeg,
            Vec3   freeAccBody, Vec3 rateOfTurnBody,
            double dt,
            bool   rotInputIsDegPerSec = true)
        {
            var output = new MruOutput();
            if (dt <= 0.0 || dt > 0.2)
            {
                output.Status = "INVALID_DT";
                return output;
            }

            double roll  = DegToRad(rollDeg);
            double pitch = DegToRad(pitchDeg);
            double yaw   = DegToRad(yawDeg);

            Vec3 accEarth  = RotateBodyToEarth(freeAccBody, roll, pitch, yaw);
            double vertAcc = accEarth.Z;

            if (Math.Abs(vertAcc) > MaxVerticalAcceleration)
            { output.Status = "ACC_SPIKE_REJECTED"; return output; }

            if (Math.Abs(vertAcc) < AccDeadband) vertAcc = 0.0;

            double accF = _accLowPass.Update(vertAcc, dt);
            _heaveVelocityRaw += accF * dt;
            double velF = _velHighPass.Update(_heaveVelocityRaw, dt);
            _heaveRaw += velF * dt;
            double heaveCg = _heaveHighPass.Update(_heaveRaw, dt);

            Vec3 rotDegS = rotInputIsDegPerSec
                ? rateOfTurnBody
                : new Vec3(RadToDeg(rateOfTurnBody.X), RadToDeg(rateOfTurnBody.Y), RadToDeg(rateOfTurnBody.Z));

            output.RollDeg    = rollDeg;
            output.PitchDeg   = pitchDeg;
            output.HeadingDeg = Normalize360(yawDeg);
            output.RollRateDegS   = rotDegS.X;
            output.PitchRateDegS  = rotDegS.Y;
            output.YawRateDegS    = rotDegS.Z;
            output.VerticalAcceleration = accF;
            output.HeaveVelocity        = velF;
            output.HeaveCg              = heaveCg;
            output.HeaveAtSensor        = HeaveAtPoint(heaveCg, SensorPositionFromCg, roll, pitch);
            output.HeaveAtBow           = HeaveAtPoint(heaveCg, BowPointFromCg,       roll, pitch);
            output.HeaveAtStern         = HeaveAtPoint(heaveCg, SternPointFromCg,     roll, pitch);
            output.HeaveAtCustomPoint   = HeaveAtPoint(heaveCg, CustomPointFromCg,    roll, pitch);
            output.Valid   = true;
            output.Status  = "VALID";
            return output;
        }

        public static double HeaveAtPoint(double heaveCg, Vec3 pt, double rollRad, double pitchRad)
            => heaveCg + pt.X * Math.Sin(pitchRad) - pt.Y * Math.Sin(rollRad);

        public static Vec3 RotateBodyToEarth(Vec3 b, double roll, double pitch, double yaw)
        {
            double cr=Math.Cos(roll), sr=Math.Sin(roll);
            double cp=Math.Cos(pitch), sp=Math.Sin(pitch);
            double cy=Math.Cos(yaw),  sy=Math.Sin(yaw);
            return new Vec3(
                (cy*cp)*b.X + (cy*sp*sr - sy*cr)*b.Y + (cy*sp*cr + sy*sr)*b.Z,
                (sy*cp)*b.X + (sy*sp*sr + cy*cr)*b.Y + (sy*sp*cr - cy*sr)*b.Z,
                  (-sp)*b.X +          (cp*sr)*b.Y +          (cp*cr)*b.Z
            );
        }

        public static double DegToRad(double d) => d * Math.PI / 180.0;
        public static double RadToDeg(double r) => r * 180.0 / Math.PI;
        public static double Normalize360(double d) { d %= 360; return d < 0 ? d+360 : d; }
    }
}
