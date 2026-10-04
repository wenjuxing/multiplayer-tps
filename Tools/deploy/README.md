# Tools/deploy —— 服务端一键发布

把 Unity 构建出来的 Linux Dedicated Server 包发布到腾讯云轻量服务器，并在服务器上做
**版本目录 + 软链切换**，因此回滚不需要重新上传。

## 文件

| 文件 | 说明 |
|---|---|
| `deploy.ps1` | **在 Windows 上运行**。打包 → 上传 → 远端部署 → 健康检查 |
| `deploy-remote.sh` | 远端执行体（由 `deploy.ps1` 自动上传；也可在服务器上手动跑） |
| `fpsgame.service` | systemd 单元模板，配合 `-InstallService` 安装 |

## 服务器目录约定

```
/opt/fpsgame                   软链，永远指向当前版本（systemd 用这个路径）
/opt/fpsgame-20261003-1530     每个版本一个独立目录
/opt/fpsgame-manual-...        首次部署时，原有的真实目录会被自动迁移成版本目录
/var/log/fpsgame/server.log    服务端日志
```

## 用法

```powershell
# 0) 先在 Unity 出包：菜单 Tools -> FPSGame-Y1 -> 打包服务端 (Linux Server)
#    （面板快捷键 Ctrl+Shift+B；会在出包前自动检查残留进程，并自动补设 Server 子目标）
#    输出固定在 Build\LinuxServer，不需要手选目录

# 1) 首次：安装 systemd 单元并发布
.\Tools\deploy\deploy.ps1 -InstallService

# 2) 以后每次发布
.\Tools\deploy\deploy.ps1

# 3) 出事回滚到上一个版本（秒级，不用重新上传）
.\Tools\deploy\deploy.ps1 -Rollback
```

> 出包前想确认客户端要连哪台服务器：看 Unity 面板「① 服务端地址」，
> 本地 `127.0.0.1` / 真实 `43.142.103.57` 一键切换。切完记得 **Ctrl+S 保存场景**。
> 不想重新出包就换地址，用启动参数 `-serverAddr <ip>`（见项目文档第 2.3 节）。

常用参数：

| 参数 | 默认值 | 说明 |
|---|---|---|
| `-Server` | `43.142.103.57` | 服务器公网 IP |
| `-User` | `ubuntu` | 登录用户（需免密 sudo） |
| `-KeyPath` | `%USERPROFILE%\.ssh\G1.pem` | SSH 私钥 |
| `-BuildDir` | `<repo>\Build\LinuxServer` | 构建产物目录 |
| `-ExeName` | `FPSGame-Y1.x86_64` | 可执行文件名，找不到会自动识别 `*.x86_64` |
| `-Keep` | `5` | 服务器上保留的历史版本数 |
| `-WithDebugSymbols` | 关 | 默认排除 `*_s.debug` / `*.pdb`，可让包小一半 |
| `-InstallService` | 关 | 顺带安装/更新 `fpsgame.service` |
| `-Rollback` | 关 | 回滚到上一个版本 |
| `-SkipHealthCheck` | 关 | 跳过发布后的状态/端口/日志检查 |

## 前提

- Windows 启用 OpenSSH 客户端（`ssh`/`scp`）与系统自带 `tar`
- 服务器上 `ubuntu` 用户可免密 `sudo`（腾讯云镜像默认满足）
- 腾讯云控制台防火墙已放行 **UDP 7777**

## 服务端常用命令（SSH 上去之后）

```bash
systemctl status fpsgame --no-pager     # 状态
sudo systemctl restart fpsgame          # 重启
tail -f /var/log/fpsgame/server.log     # 实时日志
ss -lunp | grep 7777                    # 确认监听
ls -1dt /opt/fpsgame-*                  # 看所有版本
```

## 注意

- 构建产物目录不要放进 `Assets/`，否则 Unity 会去导入这些文件
- 重新构建客户端/服务端前先关掉正在运行的旧进程，否则会报 “文件正在被使用”
- `*.pem` 私钥已在根 `.gitignore` 中排除，不要提交
