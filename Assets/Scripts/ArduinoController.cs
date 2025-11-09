using System;
using System.IO.Ports;
using UnityEngine;
using System.Collections;

public class ArduinoController : MonoBehaviour
{
    [Header("Serial Communication")]
    public string portName = "COM6";
    public int baudRate = 115200;
    public bool logSerialData = true;
    
    private SerialPort serialPort;
    private bool isConnected = false;
    
    // Parsed sensor data
    public float PressureReading { get; private set; } = 0f;
    public long EncoderCount { get; private set; } = 0;
    public bool ButtonPressed { get; private set; } = false;
    public bool PreviousButtonPressed { get; private set; } = false;
    
    // Events for sensor data updates
    public event System.Action<float> OnPressureUpdated;
    public event System.Action<long> OnEncoderUpdated;
    public event System.Action<bool, bool> OnButtonUpdated; // current, previous
    
    // Connection status
    public bool IsConnected => isConnected;
    
    // Timing for sending commands
    private float lastDistanceSendTime = 0f;
    private float distanceSendInterval = 0.05f; // Send distance every 50ms
    
    void Start()
    {
        ConnectToArduino();
        StartCoroutine(SerialReadCoroutine());
    }
    
    void Update()
    {
        // Debug input
        if (Input.GetKeyDown(KeyCode.R))
        {
            ReconnectSerial();
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            TestSerialSend();
        }
    }
    
    public void ConnectToArduino()
    {
        try
        {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.ReadTimeout = 100;
            serialPort.WriteTimeout = 100;
            serialPort.Open();
            isConnected = true;
            Debug.Log($"Connected to Arduino on {portName}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to connect to Arduino: {e.Message}");
            isConnected = false;
        }
    }
    
    public void SendDistanceCommand(float distance)
    {
        if (!isConnected || Time.time - lastDistanceSendTime < distanceSendInterval)
            return;
            
        try
        {
            string command = $"M{distance * 1000:F1}\n";
            serialPort.Write(command);
            lastDistanceSendTime = Time.time;
            
            if (logSerialData)
            {
                Debug.Log($"Sent to Arduino: {command.Trim()}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error sending to Arduino: {e.Message}");
            isConnected = false;
        }
    }
    
    public void SendCustomCommand(string command)
    {
        if (!isConnected) return;
        
        try
        {
            serialPort.Write(command + "\n");
            
            if (logSerialData)
            {
                Debug.Log($"Sent custom command to Arduino: {command}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error sending custom command to Arduino: {e.Message}");
            isConnected = false;
        }
    }
    
    IEnumerator SerialReadCoroutine()
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
                            if (logSerialData) Debug.Log($"Received: {data}");
                        }
                    }
                }
                catch (TimeoutException) { }
                catch (Exception e)
                {
                    Debug.LogError($"Error reading from Arduino: {e.Message}");
                    isConnected = false;
                }
            }
            yield return new WaitForSeconds(0.01f);
        }
    }
    
    void ParseSensorData(string data)
    {
        if (string.IsNullOrEmpty(data)) return;

        if (logSerialData)
        {
            Debug.Log($"Received from Arduino: {data}");
        }

        // Parse format: "P123|E123|B1"
        try
        {
            string[] parts = data.Split('|');
            
            float previousPressure = PressureReading;
            long previousEncoder = EncoderCount;
            PreviousButtonPressed = ButtonPressed;

            foreach (string part in parts)
            {
                if (part.StartsWith("P"))
                {
                    if (float.TryParse(part.Substring(1), out float pressure))
                    {
                        PressureReading = pressure;
                        OnPressureUpdated?.Invoke(pressure);
                    }
                }
                else if (part.StartsWith("E"))
                {
                    if (long.TryParse(part.Substring(1), out long encoder))
                    {
                        EncoderCount = encoder;
                        OnEncoderUpdated?.Invoke(encoder);
                    }
                }
                else if (part.StartsWith("B"))
                {
                    if (int.TryParse(part.Substring(1), out int button))
                    {
                        ButtonPressed = (button == 0);
                        OnButtonUpdated?.Invoke(ButtonPressed, PreviousButtonPressed);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error parsing sensor data '{data}': {e.Message}");
        }
    }
    
    public void TestSerialSend()
    {
        if (isConnected)
        {
            try
            {
                serialPort.Write("TEST\n");
                Debug.Log("Sent test command to Arduino");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error sending test command: {e.Message}");
            }
        }
    }
    
    public void ReconnectSerial()
    {
        if (isConnected && serialPort != null)
        {
            serialPort.Close();
            isConnected = false;
        }

        ConnectToArduino();
    }
    
    void OnApplicationQuit()
    {
        if (isConnected && serialPort != null)
        {
            serialPort.Close();
        }
    }

    void OnDestroy()
    {
        if (isConnected && serialPort != null)
        {
            serialPort.Close();
        }
    }
    
    // GUI for debugging Arduino connection
    public void DrawArduinoGUI()
    {
        GUILayout.Label("=== Arduino Controller ===");
        GUILayout.Label($"Serial: {(isConnected ? "Connected" : "Disconnected")}");
        GUILayout.Label($"Pressure: {PressureReading:F1}");
        GUILayout.Label($"Encoder: {EncoderCount}");
        GUILayout.Label($"Button: {(ButtonPressed ? "Pressed" : "Released")}");
        
        if (GUILayout.Button("Test Send (Space)"))
        {
            TestSerialSend();
        }
        
        if (GUILayout.Button("Reconnect (R)"))
        {
            ReconnectSerial();
        }
    }
}