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

    public class LowPassFilter
    {
        private readonly double _cutoffHz;
        private bool _init;
        private double _y;
        public LowPassFilter(double cutoffHz) { _cutoffHz = cutoffHz; }

        public double Update(double x, double dt)
        {
            if (!_init) { _y = x; _init = true; return _y; }
            double rc = 1.0 / (2.0 * Math.PI * _cutoffHz);
            double alpha = dt / (rc + dt);
            _y += alpha * (x - _y);
            return _y;
        }
        public void Reset(double value = 0) { _y = value; _init = false; }
    }

    public class HighPassFilter
    {
        private readonly double _cutoffHz;
        private bool _init;
        private double _prevX, _prevY;
        public HighPassFilter(double cutoffHz) { _cutoffHz = cutoffHz; }

        public double Update(double x, double dt)
        {
            if (!_init) { _prevX = x; _prevY = 0; _init = true; return 0; }
            double rc = 1.0 / (2.0 * Math.PI * _cutoffHz);
            double alpha = rc / (rc + dt);
            double y = alpha * (_prevY + x - _prevX);
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
        public bool Valid;
        public string Status = "INIT";
    }

    public class XsensMruProcessor
    {
        // Vị trí lắp đặt (mét, theo hệ tàu: X=mũi+, Y=mạn phải+, Z=lên+)
        public Vec3 SensorPositionFromCg = new Vec3(0, 0, 0);
        public Vec3 BowPointFromCg = new Vec3(0, 0, 0);
        public Vec3 SternPointFromCg = new Vec3(0, 0, 0);
        public Vec3 CustomPointFromCg = new Vec3(0, 0, 0);

        // Giới hạn an toàn chặn dị thường
        public double MaxVerticalAcceleration = 15.0;

        // BỘ LỌC GIA TỐC: Cắt nhiễu cơ khí (LowPass) và Cắt rò rỉ DC (HighPass)
        private readonly LowPassFilter _accLowPass = new LowPassFilter(2.0);
        private readonly HighPassFilter _accHighPass = new HighPassFilter(0.05);
        private readonly LowPassFilter _heaveLowPass = new LowPassFilter(0.5);

        // Biến tích phân
        private double _heaveVelocityRaw;
        private double _heaveRaw;

        // BÍ QUYẾT CHUẨN CÔNG NGHIỆP: Thời gian ổn định màng lọc (Settling Time)
        private int _calibFrames = 0;
        private const int CalibrationRequiredFrames = 500; // Đợi 5 giây (ở 100Hz) để bộ lọc xả hết rác khởi động

        public void Reset()
        {
            _heaveVelocityRaw = 0;
            _heaveRaw = 0;
            _accLowPass.Reset();
            _accHighPass.Reset();
            _heaveLowPass.Reset();
            _calibFrames = 0;
        }

        public MruOutput UpdateEuler(
            double rollDeg, double pitchDeg, double yawDeg,
            Vec3 freeAccBody, Vec3 rateOfTurnDegS, double dt)
        {
            MruOutput output = new MruOutput();

            if (dt <= 0 || dt > 0.2)
            {
                output.Valid = false;
                output.Status = "INVALID_DT";
                return output;
            }

            double roll = DegToRad(rollDeg);
            double pitch = DegToRad(pitchDeg);
            double yaw = DegToRad(yawDeg);

            // Chuyển hệ tọa độ
            Vec3 accEarth = RotateBodyToEarth(freeAccBody, roll, pitch, yaw);
            double verticalAcc = accEarth.Z;

            // Loại bỏ đứt gãy tín hiệu bất thường
            if (Math.Abs(verticalAcc) > MaxVerticalAcceleration)
            {
                output.Valid = false;
                output.Status = "ACC_SPIKE_REJECTED";
                return output;
            }

            // 1. CHẠY BỘ LỌC GIA TỐC
            // Tuyệt đối không dùng If(Deadband) ở đây để tránh nhiễu xung vuông gây loạn số
            double accFiltered = _accLowPass.Update(verticalAcc, dt);
            accFiltered = _accHighPass.Update(accFiltered, dt);

            // 2. GIAI ĐOẠN CALIBRATION KHI MỚI BẬT THIẾT BỊ
            // Bỏ qua tích phân trong 5s đầu để triệt tiêu nguyên nhân gây trôi liên tục
            bool isCalibrating = _calibFrames < CalibrationRequiredFrames;
            if (isCalibrating)
            {
                _calibFrames++;
                _heaveVelocityRaw = 0;
                _heaveRaw = 0;
            }

            // 3. TÍCH PHÂN WASHOUT FILTER (Thuật toán chính)
            // Tích phân vận tốc và kéo mượt về 0
            _heaveVelocityRaw += accFiltered * dt;
            _heaveVelocityRaw *= 0.995;

            // Tích phân vị trí và kéo mượt về 0 để bảo toàn sóng dài (swell)
            _heaveRaw += _heaveVelocityRaw * dt;
            _heaveRaw *= 0.999;

            // 4. LÀM MƯỢT CUỐI CÙNG & DEADBAND GIAO DIỆN
            double heaveCg = _heaveRaw;

            // Chỉ ép cứng về 0 khi giá trị dao động dưới 1.5 cm để màn hình Helideck tĩnh tuyệt đối lúc biển lặng
            if (Math.Abs(heaveCg) < 0.015)
            {
                heaveCg = 0;
            }

            output.RollDeg = rollDeg;
            output.PitchDeg = pitchDeg;
            output.HeadingDeg = Normalize360(yawDeg);
            output.RollRateDegS = rateOfTurnDegS.X;
            output.PitchRateDegS = rateOfTurnDegS.Y;
            output.YawRateDegS = rateOfTurnDegS.Z;

            output.VerticalAcceleration = accFiltered;
            output.HeaveVelocity = _heaveVelocityRaw;
            output.HeaveCg = heaveCg;

            // Tính Heave phụ trợ tại các điểm trên boong
            output.HeaveAtSensor = HeaveAtPoint(heaveCg, SensorPositionFromCg, roll, pitch);
            output.HeaveAtBow = HeaveAtPoint(heaveCg, BowPointFromCg, roll, pitch);
            output.HeaveAtStern = HeaveAtPoint(heaveCg, SternPointFromCg, roll, pitch);
            output.HeaveAtCustomPoint = HeaveAtPoint(heaveCg, CustomPointFromCg, roll, pitch);

            output.Valid = true;
            output.Status = isCalibrating
                ? $"CALIBRATING {(_calibFrames * 100) / CalibrationRequiredFrames}%"
                : "VALID";

            return output;
        }

        public static double HeaveAtPoint(double heaveCg, Vec3 pt, double rollRad, double pitchRad)
            => heaveCg + pt.X * Math.Sin(pitchRad) - pt.Y * Math.Sin(rollRad);

        public static Vec3 RotateBodyToEarth(Vec3 b, double roll, double pitch, double yaw)
        {
            double cr = Math.Cos(roll), sr = Math.Sin(roll);
            double cp = Math.Cos(pitch), sp = Math.Sin(pitch);
            double cy = Math.Cos(yaw), sy = Math.Sin(yaw);
            return new Vec3(
                (cy * cp) * b.X + (cy * sp * sr - sy * cr) * b.Y + (cy * sp * cr + sy * sr) * b.Z,
                (sy * cp) * b.X + (sy * sp * sr + cy * cr) * b.Y + (sy * sp * cr - cy * sr) * b.Z,
                  (-sp) * b.X + (cp * sr) * b.Y + (cp * cr) * b.Z
            );
        }

        public static double DegToRad(double d) => d * Math.PI / 180.0;
        public static double RadToDeg(double r) => r * 180.0 / Math.PI;
        public static double Normalize360(double d) { d %= 360; return d < 0 ? d + 360 : d; }
    }
}
