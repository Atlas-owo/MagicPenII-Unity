import serial
import time
import csv
import argparse
import matplotlib.pyplot as plt

def run_test(port, baudrate, num_cycles, out_file):
    print(f"Connecting to {port} at {baudrate} baud...")
    try:
        ser = serial.Serial(port, baudrate, timeout=1)
    except Exception as e:
        print(f"Failed to connect to {port}: {e}")
        return

    time.sleep(2) # Wait for Arduino to reset
    print("Connection established. Ensure motor is initially positioned at home (0mm) if possible.")
    time.sleep(1)

    all_data = []

    for cycle in range(1, num_cycles + 1):
        print(f"\n--- Cycle {cycle}/{num_cycles} ---")
        
        # 1. Extend to 70mm
        print("-> Extending to 70.0mm...")
        data_ext = record_movement(ser, target_dist=70.0)
        for d in data_ext:
            all_data.append((cycle, "Extending") + d)
            
        time.sleep(1.0) # Settle time
        
        # 2. Retract to 0mm
        print("<- Retracting to 0.0mm...")
        data_ret = record_movement(ser, target_dist=0.0)
        for d in data_ret:
            all_data.append((cycle, "Retracting") + d)
            
        time.sleep(1.0) # Settle time
    
    ser.close()

    if not all_data:
        print("No data collected.")
        return

    print(f"\nCollected {len(all_data)} data points. Saving to {out_file}...")
    with open(out_file, "w", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["Cycle", "Direction", "Time_ms", "Encoder_Count", "Target_Count"])
        writer.writerows(all_data)
    
    print("Plotting results...")
    plot_data(out_file, num_cycles)

def record_movement(ser, target_dist):
    cmd = f"X{target_dist}\n"
    ser.write(cmd.encode("utf-8"))
    
    data = []
    test_started = False
    
    try:
        while True:
            if ser.in_waiting > 0:
                line = ser.readline().decode("utf-8", errors="ignore").strip()
                if line == "TEST_START":
                    test_started = True
                elif line == "TEST_END":
                    break
                elif line.startswith("DATA,") and test_started:
                    parts = line.split(",")
                    if len(parts) == 4:
                        t = float(parts[1])
                        enc = int(parts[2])
                        tgt = int(parts[3])
                        data.append((t, enc, tgt))
    except KeyboardInterrupt:
        print("Interrupted by user.")
    
    return data

def plot_data(file_path, num_cycles):
    ext_data = {}
    ret_data = {}

    with open(file_path, "r") as f:
        reader = csv.reader(f)
        next(reader) # header
        for row in reader:
            cycle = int(row[0])
            direction = row[1]
            t = float(row[2]) / 1000.0
            p = int(row[3])
            tgt = int(row[4])
            
            if direction == "Extending":
                if cycle not in ext_data: ext_data[cycle] = ([], [], [])
                ext_data[cycle][0].append(t)
                ext_data[cycle][1].append(p)
                ext_data[cycle][2].append(tgt)
            else:
                if cycle not in ret_data: ret_data[cycle] = ([], [], [])
                ret_data[cycle][0].append(t)
                ret_data[cycle][1].append(p)
                ret_data[cycle][2].append(tgt)
                
    fig, (ax1, ax2) = plt.subplots(1, 2, figsize=(14, 6))
    
    # Extending plot
    for c in range(1, num_cycles + 1):
        if c in ext_data:
            # Shift time to start from 0 for each plot line
            t0 = ext_data[c][0][0] if len(ext_data[c][0]) > 0 else 0
            t_shifted = [t_i - t0 for t_i in ext_data[c][0]]
            ax1.plot(t_shifted, ext_data[c][1], label=f'Cycle {c} Pos', alpha=0.7)
            if c == 1:
                ax1.plot(t_shifted, ext_data[c][2], 'k--', alpha=0.5, label='Target')
                
    ax1.set_title("Extending to 70mm")
    ax1.set_xlabel("Time (s)")
    ax1.set_ylabel("Encoder Count")
    ax1.grid(True)
    ax1.legend()
    
    # Retracting plot
    for c in range(1, num_cycles + 1):
        if c in ret_data:
            t0 = ret_data[c][0][0] if len(ret_data[c][0]) > 0 else 0
            t_shifted = [t_i - t0 for t_i in ret_data[c][0]]
            ax2.plot(t_shifted, ret_data[c][1], label=f'Cycle {c} Pos', alpha=0.7)
            if c == 1:
                ax2.plot(t_shifted, ret_data[c][2], 'k--', alpha=0.5, label='Target')
                
    ax2.set_title("Retracting to 0mm")
    ax2.set_xlabel("Time (s)")
    ax2.set_ylabel("Encoder Count")
    ax2.grid(True)
    ax2.legend()
    
    plt.tight_layout()
    plt.show()

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Measure Arduino Motor Response")
    parser.add_argument("--port", type=str, default="COM5", help="Serial port")
    parser.add_argument("--baud", type=int, default=115200, help="Baud rate")
    parser.add_argument("--cycles", type=int, default=5, help="Number of test cycles")
    parser.add_argument("--out", type=str, default="response_data.csv", help="Output CSV file path")
    
    args = parser.parse_args()
    run_test(args.port, args.baud, args.cycles, args.out)
