"""
NMEA Sender — gửi dữ liệu test vào virtual COM port (đầu kia của com0com pair).
Cách dùng:
  python nmea_sender.py COM11   # gửi GPS vào COM11 (app đọc COM10)
  python nmea_sender.py COM21   # gửi WIND vào COM21 (app đọc COM20)
  python nmea_sender.py COM31   # gửi HEADING vào COM31 (app đọc COM30)
  python nmea_sender.py COM11 alarm   # gửi giá trị vượt ngưỡng để test alarm
"""

import serial, time, sys, math, random

def checksum(sentence):
    cs = 0
    for c in sentence[1:]:   # bỏ $
        cs ^= ord(c)
    return f"{sentence}*{cs:02X}\r\n"

port_name = sys.argv[1] if len(sys.argv) > 1 else "COM11"
mode      = sys.argv[2] if len(sys.argv) > 2 else "normal"

port = serial.Serial(port_name, baudrate=9600, timeout=1)
print(f"Sending NMEA to {port_name} [{mode} mode]. Ctrl+C to stop.")

t = 0
try:
    while True:
        t += 0.1

        # GPS — $GPGGA + $GPVTG
        lat   = "1045.500"
        lon   = "10640.200"
        speed = 5.0 + math.sin(t * 0.1) * 2   # 3–7 knots

        gga = checksum(f"$GPGGA,083226.00,{lat},N,{lon},E,1,08,1.0,10.0,M,0.0,M,,")
        vtg = checksum(f"$GPVTG,360.0,T,348.7,M,{speed:.1f},N,{speed*1.852:.1f},K")

        # WIND — $WIMWV
        if mode == "alarm":
            wind_spd = 50.0 + random.uniform(-2, 2)   # vượt WindMax=40
            wind_dir = 135.0
        else:
            wind_spd = 15.0 + math.sin(t * 0.05) * 8   # 7–23 m/s
            wind_dir = 120.0 + math.sin(t * 0.03) * 30

        mwv = checksum(f"$WIMWV,{wind_dir:.1f},R,{wind_spd:.1f},M,A")

        # HEADING — $HEHDT
        heading = 180.0 + math.sin(t * 0.02) * 5

        hdt = checksum(f"$HEHDT,{heading:.1f},T")

        # Gửi tất cả — app đọc port nào thì gửi sentence tương ứng
        for sentence in [gga, vtg, mwv, hdt]:
            port.write(sentence.encode("ascii"))

        print(f"\r[{t:6.1f}s] Wind={wind_spd:.1f}m/s Dir={wind_dir:.0f}° Hdg={heading:.1f}° Spd={speed:.1f}kn", end="")
        time.sleep(0.1)

except KeyboardInterrupt:
    print("\nStopped.")
finally:
    port.close()
