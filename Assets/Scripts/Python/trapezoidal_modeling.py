# %% [markdown]
# # 分段一阶惯性系统建模 (Segmented First-Order Modeling)
# 既然我们明确了真实的动作包含了“起步加速 -> 匀速巡航 -> 刹车减速 -> 到达”，那么用一整根一阶曲线（甚至一整根双 Tanh 曲线）都只是宏观拟合。
# 如果想得到真实的机电物理特性，最严谨的做法就是**分段切片**。
# 
# 1. **加速段**: 在 PWM 为 +255 时，电机的加速度响应 $v(t) = v_{max}(1 - e^{-t/\tau_{accel}})$
# 2. **减速段**: 在 PWM 为相反数反接制动时，相当于向电机施加了一个反向的极限目标速度 $v_{target}$。速度由当时的 $v_0$ 迅速衰减跌落：$v(t) = v_{target} + (v_0 - v_{target}) e^{-t/\tau_{decel}}$

# %%
import pandas as pd
import numpy as np
import matplotlib.pyplot as plt
from scipy.optimize import curve_fit
from scipy.signal import savgol_filter

np.seterr(invalid='ignore', divide='ignore')

# 1. 载入数据
try:
    df = pd.read_csv('../../../response_data.csv')
except FileNotFoundError:
    df = pd.read_csv('response_data.csv')

# ================================
# 分段物理模型定义
# ================================

def first_order_accel(t, vmax, tau, t0):
    """
    加速段一阶模型 (正向阶跃给定极速)
    """
    v = np.zeros_like(t)
    mask = t >= t0
    if np.any(mask):
        v[mask] = vmax * (1 - np.exp(-(t[mask] - t0) / tau))
    return v

def first_order_decel(t, v0, v_target, tau, t0):
    """
    带反接制动的减速一阶模型
    真实物理中，主动刹车是给的反向恒定电压，相当于阶跃给定了负目标极速 v_target。
    实际速度从 v0 按照时间常数 tau 逐渐向假想的负数 v_target 暴跌，直到速度清零。
    """
    v = np.full_like(t, v0, dtype=float)
    mask = t >= t0
    if np.any(mask):
        v[mask] = v_target + (v0 - v_target) * np.exp(-(t[mask] - t0) / tau)
    return v

# %% [markdown]
# ## 自动切分与拟合

# %%
def fit_segmented_first_order(direction='Extending'):
    df_sub = df[(df['Cycle'] == 1) & (df['Direction'] == direction)].copy()
    if len(df_sub) == 0: return

    t_raw = (df_sub['Time_ms'].values - df_sub['Time_ms'].values[0]) / 1000.0
    # 根据 Nano_speed.ino 的换算公式: mm = count * 0.0084 + 0.5
    y_raw = df_sub['Encoder_Count'].values * 0.0084 + 0.5
    
    dt = np.diff(t_raw)
    dt[dt == 0] = 1e-6
    v_raw = np.diff(y_raw) / dt
    t_v = t_raw[:-1] + dt/2
    
    # 无论是伸长还是缩短，速度绝对值的波形长相是一样的，为了方便处理，全部取绝对值
    v_abs = np.abs(v_raw)
    
    window = min(21, len(v_abs) if len(v_abs) % 2 != 0 else len(v_abs) - 1)
    v_smooth = savgol_filter(v_abs, window_length=window, polyorder=2) if len(v_abs) > window else v_abs

    # ----- 自动数据切分 -----
    vmax_actual = np.max(v_smooth)
    idx_max = np.argmax(v_smooth)
    t_max = t_v[idx_max]

    # 切分加速段: 从时间 0 开始，直到速度攀升到 90% 极速的点
    idx_accel_end = np.where(v_smooth[:idx_max] >= vmax_actual * 0.9999)[0]
    idx_accel_end = idx_accel_end[0] if len(idx_accel_end) > 0 else idx_max
    
    if idx_accel_end < 5: 
        idx_accel_end = idx_max # 防止切得太短

    t_accel = t_v[:idx_accel_end]
    v_accel = v_smooth[:idx_accel_end]
    
    # 切分减速段: 寻找平台期结束后，速度从顶峰跌落到 90% 的那个点作为刹车起始点
    idx_decel_start = np.where(v_smooth[idx_max:] <= vmax_actual * 0.91)[0]
    idx_decel_start = idx_max + idx_decel_start[0] if len(idx_decel_start) > 0 else idx_max

    t_decel = t_v[idx_decel_start:]
    v_decel = v_smooth[idx_decel_start:]
    
    v0_decel = v_smooth[idx_decel_start]
    t0_decel = t_v[idx_decel_start]

    # ----- 拟合加速段 -----
    # 参数: [vmax, tau, t0]
    p0_a = [vmax_actual, 0.05, 0.0]
    bounds_a = ([vmax_actual*0.8, 0.001, -0.1], [vmax_actual*1.5, 1.0, 0.5])
    try:
        popt_a, _ = curve_fit(first_order_accel, t_accel, v_accel, p0=p0_a, bounds=bounds_a)
    except Exception as e:
        print("加速段拟合失败:", e)
        popt_a = p0_a

    # ----- 拟合减速段 -----
    # 参数: [v_target, tau_d, t0_d]
    # v_target 必定是个负数，因为主动刹车给了倒挡反接 PWM
    p0_d = [-vmax_actual*0.5, 0.05, t0_decel]
    bounds_d = ([-10000.0, 0.001, t0_decel-0.1], [0.0, 1.0, t0_decel+0.1])

    # 这里我们把起始速度 v0_decel 写死（因为数据可以直接指定起点），只让算法去找衰减常数和假想目标
    model_d = lambda t, vt, tau, t0: first_order_decel(t, v0_decel, vt, tau, t0)
    try:
        popt_d_sub, _ = curve_fit(model_d, t_decel, v_decel, p0=p0_d, bounds=bounds_d)
        # [v0, v_target, tau_d, t0_d]
        popt_d = [v0_decel, popt_d_sub[0], popt_d_sub[1], popt_d_sub[2]]
    except Exception as e:
        print("减速段拟合失败:", e)
        popt_d = [v0_decel, p0_d[0], p0_d[1], p0_d[2]]

    print(f"=== {direction} 分段机电动力学参数 ===")
    print(f"[加速段] 稳态最高速极限 (v_max): {popt_a[0]:.1f} mm/s")
    print(f"         加速期时间常数 (τ_a): {popt_a[1]:.4f} s")
    print(f"         动作死区时间   (t_0): {popt_a[2]:.4f} s")
    print()
    print(f"[减速段] 刹车判定起始点 (v_0)  : {popt_d[0]:.1f} mm/s")
    print(f"         刹车反接假想极速      : {popt_d[1]:.1f} mm/s")
    print(f"         刹车期时间常数 (τ_d): {popt_d[2]:.4f} s")
    print(f"         刹车指令下发时 (t_b): {popt_d[3]:.4f} s")
    print("-" * 50)

    # ----- 分段绘图 -----
    fig, ax = plt.subplots(figsize=(12, 6))
    
    # 原数据
    ax.plot(t_v, v_abs, 'g.', alpha=0.15, label='|Raw Derivative Speed|')
    ax.plot(t_v, v_smooth, 'lightgray', lw=2, alpha=0.6, label='Smoothed Trace')
    
    # 截取加速段单独绘出
    t_plot_a = np.linspace(0, t_v[idx_accel_end], 200)
    ax.plot(t_plot_a, first_order_accel(t_plot_a, *popt_a), 'r-', lw=2, 
            label=f'Accel Phase Fit ($\\tau_a$={popt_a[1]:.3f}s)')
    
    # 截取减速段绘出
    t_plot_d = np.linspace(t_v[idx_decel_start], t_v[-1], 200)
    ax.plot(t_plot_d, first_order_decel(t_plot_d, *popt_d), 'b-', lw=2, 
            label=f'Brake Phase Fit ($\\tau_d$={popt_d[2]:.3f}s)')
    
    # 辅助线
    ax.axvline(t0_decel, color='purple', linestyle='--', alpha=0.5, label='Brake Trigger Time')
    
    ax.set_title(f'{direction} - Segmented First-Order Speed Fit')
    ax.set_xlabel('Time (s)')
    ax.set_ylabel('|Absolute Speed| (mm/s)')
    ax.legend()
    ax.grid(True)
    plt.tight_layout()
    plt.show()

# %%
fit_segmented_first_order('Extending')
fit_segmented_first_order('Retracting')
