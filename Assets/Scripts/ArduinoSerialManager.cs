using System;
using System.IO.Ports;
using UnityEngine;
using System.Collections;

/// <summary>
/// Manages serial communication with Arduino for the haptic pen system.
/// Handles connection, sending commands, and parsing incoming sensor data.
/// </summary>
public class ArduinoSerialManager : MonoBehaviour
{
    [Header("Serial Communication")]
    public string portName = "COM6";
    public int baudRate = 115200;

    [Header("Communication Settings")]
    public float sendInterval = 0.05f; // Send distance every 50ms

    [Header("Debug")]
    public bool logSerialData = false;

    // Serial port
    private SerialPort serialPort;
    private bool isConnected = false;
    private float lastSendTime = 0f;

    // Parsed sensor data
    private float pressureReading = 0f;
    private long encoderCount = 0;
    private float realDistance = 0f;
    private bool buttonPressed = false;
    private bool homeButtonPressed = false;

    // Public properties for external access
    public bool IsConnected => isConnected;
    public float PressureReading => pressureReading;
    public long EncoderCount => encoderCount;
    public float RealDistance => realDistance;
    public bool ButtonPressed => buttonPressed;
    public bool HomeButtonPressed => homeButtonPressed;

    // Events for data updates
    public event Action<float> OnPressureUpdated;
    public event Action<long> OnEncoderUpdated;
    public event Action<float> OnRealDistanceUpdated;
    public event Action<bool> OnButtonUpdated;
    public event Action<bool> OnHomeButtonUpdated;

    void Start()
    {
        Connect();
        StartCoroutine(SerialReadCoroutine());
    }

    /// <summary>
    /// Connect to Arduino via serial port.
    /// </summary>
    public void Connect()
    {
        try
        {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.ReadTimeout = 100;
            serialPort.WriteTimeout = 100;
            serialPort.Open();
            isConnected = true;
            Debug.Log($"[ArduinoSerialManager] Connected to Arduino on {portName}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ArduinoSerialManager] Failed to connect to Arduino: {e.Message}");
            isConnected = false;
        }
    }

    /// <summary>
    /// Disconnect and reconnect to Arduino.
    /// </summary>
    public void Reconnect()
    {
        if (isConnected && serialPort != null)
        {
            serialPort.Close();
            isConnected = false;
        }

        Connect();
    }

    /// <summary>
    /// Send distance command to Arduino.
    /// Format: M{distance_in_mm}
    /// </summary>
    /// <param name="distanceInMeters">Distance in meters</param>
    public void SendDistance(float distanceInMeters)
    {
        if (!isConnected || Time.time - lastSendTime < sendInterval)
            return;

        try
        {
            // Convert to millimeters and send with 'M' prefix
            string command = $"M{distanceInMeters * 1000:F1}\n";
            serialPort.Write(command);
            lastSendTime = Time.time;

            if (logSerialData)
            {
                Debug.Log($"[ArduinoSerialManager] Sent: {command.Trim()}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ArduinoSerialManager] Error sending to Arduino: {e.Message}");
            isConnected = false;
        }
    }

    /// <summary>
    /// Send a custom command to Arduino.
    /// </summary>
    /// <param name="command">Command string to send</param>
    public void SendCommand(string command)
    {
        if (!isConnected) return;

        try
        {
            serialPort.Write(command + "\n");

            if (logSerialData)
            {
                Debug.Log($"[ArduinoSerialManager] Sent custom command: {command}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ArduinoSerialManager] Error sending command: {e.Message}");
        }
    }

    /// <summary>
    /// Send test command to Arduino.
    /// </summary>
    public void SendTestCommand()
    {
        SendCommand("TEST");
        Debug.Log("[ArduinoSerialManager] Sent test command to Arduino");
    }

    /// <summary>
    /// Coroutine for continuously reading serial data from Arduino.
    /// </summary>
    private IEnumerator SerialReadCoroutine()
    {
        while (true)
        {
            if (isConnected)
            {
                try
                {
                    // Clear buffer by reading all available data
                    while (serialPort.BytesToRead > 0)
                    {
                        string data = serialPort.ReadLine();
                        // Only process the last (most recent) line
                        if (serialPort.BytesToRead == 0)
                        {
                            ParseSensorData(data);
                            if (logSerialData) Debug.Log($"[ArduinoSerialManager] Received: {data}");
                        }
                    }
                }
                catch (TimeoutException) { }
                catch (Exception e)
                {
                    Debug.LogError($"[ArduinoSerialManager] Error reading from Arduino: {e.Message}");
                    isConnected = false;
                }
            }
            yield return new WaitForSeconds(0.01f);
        }
    }

    /// <summary>
    /// Parse incoming sensor data from Arduino.
    /// Format: P{pressure}|E{encoder}|D{distance}|B{button}|H{homeButton}
    /// </summary>
    private void ParseSensorData(string data)
    {
        if (string.IsNullOrEmpty(data)) return;

        try
        {
            string[] parts = data.Split('|');

            foreach (string part in parts)
            {
                if (part.StartsWith("P"))
                {
                    if (float.TryParse(part.Substring(1), out float pressure))
                    {
                        pressureReading = pressure;
                        OnPressureUpdated?.Invoke(pressureReading);
                    }
                }
                else if (part.StartsWith("E"))
                {
                    if (long.TryParse(part.Substring(1), out long encoder))
                    {
                        encoderCount = encoder;
                        OnEncoderUpdated?.Invoke(encoderCount);
                    }
                }
                else if (part.StartsWith("D"))
                {
                    if (float.TryParse(part.Substring(1), out float distance))
                    {
                        realDistance = distance;
                        OnRealDistanceUpdated?.Invoke(realDistance);
                    }
                }
                else if (part.StartsWith("B"))
                {
                    if (int.TryParse(part.Substring(1), out int button))
                    {
                        buttonPressed = (button == 1);
                        OnButtonUpdated?.Invoke(buttonPressed);
                    }
                }
                else if (part.StartsWith("H"))
                {
                    if (int.TryParse(part.Substring(1), out int homeButton))
                    {
                        homeButtonPressed = (homeButton == 1);
                        OnHomeButtonUpdated?.Invoke(homeButtonPressed);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ArduinoSerialManager] Error parsing sensor data '{data}': {e.Message}");
        }
    }

    void OnApplicationQuit()
    {
        CloseConnection();
    }

    void OnDestroy()
    {
        CloseConnection();
    }

    private void CloseConnection()
    {
        if (isConnected && serialPort != null)
        {
            serialPort.Close();
            isConnected = false;
        }
    }
}
