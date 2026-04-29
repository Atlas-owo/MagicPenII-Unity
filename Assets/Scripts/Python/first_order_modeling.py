# %% [markdown]
# # 一阶惯性系统建模 (First Order Inertial System Modeling)
# 此文件可直接在 VSCode 中点击 `Run Cell` 像 Jupyter Notebook 一样运行，
# 或直接复制所有代码到一个新的 .ipynb 文件中运行。

# %%
import pandas as pd
import numpy as np
import matplotlib.pyplot as plt
from scipy.optimize import curve_fit
from scipy.signal import savgol_filter

# 用来抑制科学计数法的警告
np.seterr(invalid='ignore', divide='ignore')

# 1. 载入数据
# 确保你在此脚本的上一级目录下有 response_data.csv，或修改正确路径
try:
    df = pd.read_csv('../../../response_data.csv')
except FileNotFoundError:
    df = pd.read_csv('response_data.csv') # 退回尝试当前目录

# ================================
# 一阶惯性系统标准方程
# ================================
# 标准公式: y(t) = y_start + K * (1 - exp(-(t-t0)/tau))
# 当 t < t0 时 (死区时间)，y(t) = y_start
def first_order_step(t, y_start, K, tau, t0):
    y = np.full_like(t, y_start, dtype=float)
    mask = t >= t0
    if np.any(mask):
        # 使用 K 作为差值增益，符号由外部定义
        y[mask] = y_start + K * (1 - np.exp(-(t[mask] - t0) / tau))
    return y

# %% [markdown]
# ## 伸长过程建模 (Extending)

# %%
# 选取第 1 次伸长过程作为建模样本
df_ext = df[(df['Cycle'] == 1) & (df['Direction'] == 'Extending')].copy()

# 将时间转换成从 0 开始的秒数
t_ext = (df_ext['Time_ms'].values - df_ext['Time_ms'].values[0]) / 1000.0
y_ext = df_ext['Encoder_Count'].values
target_ext = df_ext['Target_Count'].values[0]
start_ext = y_ext[0]

# 我们计算差分速度 (前向差分)
dt_ext = np.diff(t_ext)
dt_ext[dt_ext == 0] = 1e-6 # 避免除零
dy_ext = np.diff(y_ext)
v_ext = dy_ext / dt_ext
t_v_ext = t_ext[:-1] + (dt_ext / 2) # 取间隔中点时间

# 使用 Savitzky-Golay 滤波器对速度进行平滑，以去除由于 Arduino 离散毫秒定时器造成的除法抖动
window_size = min(21, len(v_ext) if len(v_ext) % 2 != 0 else len(v_ext) - 1)
v_ext_smooth = savgol_filter(v_ext, window_length=window_size, polyorder=2) if len(v_ext) > window_size else v_ext

# --- 曲线拟合 ---
# 待求参数: y_start, K, tau, t0
# 猜测: 起点约等于真实起点, 增益约等于真实落差, tau通常在0.1s左右, t0也是0.1s左右
p0_ext = [start_ext, target_ext - start_ext, 0.1, 0.05]
# 设定边界约束: (下界, 上界)
bounds_ext = (
    [start_ext-50, 0, 0.001, 0.0],
    [start_ext+50, 20000, 2.0, 1.0]
)

popt_ext, _ = curve_fit(first_order_step, t_ext, y_ext, p0=p0_ext, bounds=bounds_ext)
y_start_fit, K_fit, tau_fit, t0_fit = popt_ext

print(f"--- 伸长过程 (Extending) 一阶系统参数 ---")
print(f"拟合起点 (y_start): {y_start_fit:.1f}")
print(f"稳态增益 (K):       {K_fit:.1f}")
print(f"时间常数 (tau):     {tau_fit:.4f} s")
print(f"系统延迟 (t0):      {t0_fit:.4f} s")

# 生成拟合曲线的高清数据
t_fine = np.linspace(t_ext[0], t_ext[-1], 500)
y_fit_fine = first_order_step(t_fine, *popt_ext)

# 理论上的速度(导数)方程
# v(t) = (K/tau) * exp(-(t-t0)/tau) 当 t >= t0
v_fit_fine = np.zeros_like(t_fine)
mask_v = t_fine >= t0_fit
v_fit_fine[mask_v] = (K_fit / tau_fit) * np.exp(-(t_fine[mask_v] - t0_fit) / tau_fit)

# ==== 绘制对比图 ====
fig, axs = plt.subplots(2, 1, figsize=(10, 8), sharex=True)

# 1. 距离(位置)响应
axs[0].plot(t_ext, y_ext, 'b.', label='Actual Position', alpha=0.5)
axs[0].plot(t_fine, y_fit_fine, 'r-', linewidth=2, label=f'Model Fit ($\\tau$={tau_fit:.3f}s)')
axs[0].axhline(target_ext, color='k', linestyle='--', label='Command Target', alpha=0.6)
axs[0].set_title('Extending: Position Step Response')
axs[0].set_ylabel('Encoder Count')
axs[0].legend()
axs[0].grid(True)

# 2. 速度响应
axs[1].plot(t_v_ext, v_ext, 'g.', label='Raw Derivative', alpha=0.1)
axs[1].plot(t_v_ext, v_ext_smooth, 'g-', label='Smoothed Actual Speed', linewidth=1.5)
axs[1].plot(t_fine, v_fit_fine, 'r-', linewidth=2, label='Model Speed (Theoretical)')
axs[1].set_title('Extending: Speed Response')
axs[1].set_xlabel('Time (s)')
axs[1].set_ylabel('Speed (Counts/s)')
axs[1].legend()
axs[1].grid(True)

plt.tight_layout()
plt.show()

# %% [markdown]
# ## 缩回过程建模 (Retracting)

# %%
# 选取第 1 次缩回过程作为建模样本
df_ret = df[(df['Cycle'] == 1) & (df['Direction'] == 'Retracting')].copy()

t_ret = (df_ret['Time_ms'].values - df_ret['Time_ms'].values[0]) / 1000.0
y_ret = df_ret['Encoder_Count'].values
target_ret = df_ret['Target_Count'].values[0]
start_ret = y_ret[0]

dt_ret = np.diff(t_ret)
dt_ret[dt_ret == 0] = 1e-6
dy_ret = np.diff(y_ret)
v_ret = dy_ret / dt_ret  # 这里速度会是负数
t_v_ret = t_ret[:-1] + (dt_ret / 2)

window_size_r = min(21, len(v_ret) if len(v_ret) % 2 != 0 else len(v_ret) - 1)
v_ret_smooth = savgol_filter(v_ret, window_length=window_size_r, polyorder=2) if len(v_ret) > window_size_r else v_ret

# 注意在缩回过程，K 应该是负值 (表示位移往下掉)
p0_ret = [start_ret, target_ret - start_ret, 0.1, 0.05]
bounds_ret = (
    [start_ret-50, -20000, 0.001, 0.0],
    [start_ret+50, 0, 2.0, 1.0] # 增益上限为0因为是往回走
)

popt_ret, _ = curve_fit(first_order_step, t_ret, y_ret, p0=p0_ret, bounds=bounds_ret)
y_start_fit_r, K_fit_r, tau_fit_r, t0_fit_r = popt_ret

print(f"--- 缩回过程 (Retracting) 一阶系统参数 ---")
print(f"拟合起点 (y_start): {y_start_fit_r:.1f}")
print(f"稳态增益 (K):       {K_fit_r:.1f}")
print(f"时间常数 (tau):     {tau_fit_r:.4f} s")
print(f"系统延迟 (t0):      {t0_fit_r:.4f} s")

# 生成拟合曲线
t_fine_r = np.linspace(t_ret[0], t_ret[-1], 500)
y_fit_fine_r = first_order_step(t_fine_r, *popt_ret)

# 理论上的速度(导数)方程
v_fit_fine_r = np.zeros_like(t_fine_r)
mask_v_r = t_fine_r >= t0_fit_r
v_fit_fine_r[mask_v_r] = (K_fit_r / tau_fit_r) * np.exp(-(t_fine_r[mask_v_r] - t0_fit_r) / tau_fit_r)


# ==== 绘制对比图 ====
fig, axs = plt.subplots(2, 1, figsize=(10, 8), sharex=True)

# 1. 距离(位置)响应
axs[0].plot(t_ret, y_ret, 'b.', label='Actual Position', alpha=0.5)
axs[0].plot(t_fine_r, y_fit_fine_r, 'r-', linewidth=2, label=f'Model Fit ($\\tau$={tau_fit_r:.3f}s)')
axs[0].axhline(target_ret, color='k', linestyle='--', label='Command Target', alpha=0.6)
axs[0].set_title('Retracting: Position Step Response')
axs[0].set_ylabel('Encoder Count')
axs[0].legend()
axs[0].grid(True)

# 2. 速度响应
axs[1].plot(t_v_ret, v_ret, 'g.', label='Raw Derivative', alpha=0.1)
axs[1].plot(t_v_ret, v_ret_smooth, 'g-', label='Smoothed Actual Speed', linewidth=1.5)
axs[1].plot(t_fine_r, v_fit_fine_r, 'r-', linewidth=2, label='Model Speed (Theoretical)')
axs[1].set_title('Retracting: Speed Response')
axs[1].set_xlabel('Time (s)')
axs[1].set_ylabel('Speed (Counts/s)')
axs[1].legend()
axs[1].grid(True)

plt.tight_layout()
plt.show()
