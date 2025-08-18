using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Ports;
using UnityEngine;

public class SerialController : MonoBehaviour
{

    // 串口配置
    [Header("串口设置")]
    [SerializeField] private string portName = "COM3"; // 串口名称，根据实际情况修改
    [SerializeField] private int baudRate = 9600; // 波特率


    // 内部变量
    private SerialPort serialPort;
    // 平面检测设置
    [Header("平面检测设置")]
    [SerializeField] private GameObject planeObject; // 拖拽平面物体到这里
    [SerializeField] private float checkInterval = 0.1f; // 检测间隔（秒）
    [SerializeField] private float raycastDistance = 100f; // 射线检测距离

    // 屏幕边界设置
    [Header("屏幕边界设置")]
    [SerializeField] private Camera mainCamera; // 主摄像机
    [SerializeField] private float screenBottomThreshold = 0.2f; // 屏幕底部阈值（0-1）

    // 发送间隔设置
    [Header("发送设置")]
    [SerializeField] private float sendInterval = 0.1f; // 发送字符的间隔时间

    // 内部变量
    private bool isAbovePlane = false;
    private bool isBelowScreen = false;
    private bool isColliding = false;
    private float lastSendTime = 0f;

    void Start()
    {

        try
        {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.Open();
            Debug.Log($"串口 {portName} 已打开");
        }
        catch (Exception e)
        {
            Debug.LogError($"无法打开串口: {e.Message}");
        }
        // 自动获取主摄像机
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        // 确保物体有Collider组件
        if (GetComponent<Collider>() == null)
        {
            Debug.LogError("空物体需要添加Collider组件才能检测碰撞！");
        }

        // 确保物体有Rigidbody组件（用于碰撞检测）
        if (GetComponent<Rigidbody>() == null)
        {
            Debug.LogWarning("添加Rigidbody组件可以提高碰撞检测的准确性");
        }

        StartCoroutine(CheckPosition());
    }

    void Update()
    {
        // 持续发送字符
        if (Time.time - lastSendTime >= sendInterval)
        {
            // 优先级：碰撞 > 平面上方 > 屏幕下方
            if (isColliding)
            {
                SendSerialCommand('f');
                lastSendTime = Time.time;
            }
            //else if (isAbovePlane)
            //{
            //    SendSerialCommand('f');
            //    lastSendTime = Time.time;
            //}
            //else if (isBelowScreen)
            //{
            //    SendSerialCommand('r');
            //    lastSendTime = Time.time;
            //}
        }
    }

    // 协程：定期检测位置
    IEnumerator CheckPosition()
    {
        while (true)
        {
            // 检测是否在平面上方
            CheckIfAbovePlane();

            // 检测是否在屏幕下方
            CheckIfBelowScreen();

            yield return new WaitForSeconds(checkInterval);
        }
    }

    void CheckIfAbovePlane()
    {
        if (planeObject != null)
        {
            // 使用射线检测是否在平面上方
            RaycastHit hit;
            Ray ray = new Ray(transform.position, Vector3.down);

            if (Physics.Raycast(ray, out hit, raycastDistance))
            {
                isAbovePlane = (hit.collider.gameObject == planeObject);
            }
            else
            {
                isAbovePlane = false;
            }
        }
    }

    void CheckIfBelowScreen()
    {
        if (mainCamera != null)
        {
            // 将世界坐标转换为视口坐标
            Vector3 viewportPos = mainCamera.WorldToViewportPoint(transform.position);

            // 检查是否在屏幕下方（视口坐标Y < threshold）
            isBelowScreen = viewportPos.y < screenBottomThreshold;
        }
    }

    // 碰撞检测 - 3D碰撞器
    void OnCollisionEnter(Collision collision)
    {
        // 确保不是与平面的碰撞
        if (collision.gameObject != planeObject)
        {
            isColliding = true;
            Debug.Log($"[碰撞开始] 与 {collision.gameObject.name} 开始碰撞");
        }
    }

    void OnCollisionStay(Collision collision)
    {
        // 确保不是与平面的碰撞
        if (collision.gameObject != planeObject)
        {
            isColliding = true;
        }
    }

    void OnCollisionExit(Collision collision)
    {
        // 确保不是与平面的碰撞
        if (collision.gameObject != planeObject)
        {
            isColliding = false;
            Debug.Log($"[碰撞结束] 与 {collision.gameObject.name} 结束碰撞");
        }
    }

    // 触发器检测 - 3D触发器
    void OnTriggerEnter(Collider other)
    {
        // 确保不是与平面的触发
        if (other.gameObject != planeObject)
        {
            isColliding = true;
            Debug.Log($"[触发开始] 与 {other.gameObject.name} 开始触发");
        }
    }

    void OnTriggerStay(Collider other)
    {
        // 确保不是与平面的触发
        if (other.gameObject != planeObject)
        {
            isColliding = true;
        }
    }

    void OnTriggerExit(Collider other)
    {
        // 确保不是与平面的触发
        if (other.gameObject != planeObject)
        {
            isColliding = false;
            Debug.Log($"[触发结束] 与 {other.gameObject.name} 结束触发");
        }
    }

    // 发送串口命令（这里只是记录，实际串口通信需要额外处理）
    void SendSerialCommand(char data)
    {
        // TODO: 在这里添加实际的串口发送代码


        if (serialPort != null && serialPort.IsOpen)
        {
            try
            {
                serialPort.Write(data.ToString());
                Debug.Log($"已发送: '{data}'");
            }
            catch (Exception e)
            {
                Debug.LogError($"串口发送失败: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning("串口未打开，无法发送数据");
        }

        // 如果使用Ardity插件，在这里调用：
        // arduinoController.SendSerialMessage(command.ToString());
    }

    void SendToSerial(char data)
    {
        if (serialPort != null && serialPort.IsOpen)
        {
            try
            {
                serialPort.Write(data.ToString());
                Debug.Log($"已发送: '{data}'");
            }
            catch (Exception e)
            {
                Debug.LogError($"串口发送失败: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning("串口未打开，无法发送数据");
        }
    }

    // 在编辑器中绘制辅助线
    void OnDrawGizmos()
    {
        // 绘制向下的射线
        Gizmos.color = isAbovePlane ? Color.green : Color.red;
        Gizmos.DrawRay(transform.position, Vector3.down * raycastDistance);

        // 绘制物体位置
        Gizmos.color = isBelowScreen ? Color.blue : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
}