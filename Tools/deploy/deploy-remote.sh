#!/usr/bin/env bash
# ===========================================================================
# FPSGame 服务端部署脚本（在服务器上以 root 运行）
#
#   sudo bash deploy-remote.sh --tar /tmp/fpsgame-20261003-1530.tar.gz --exe FPSGame-Y1.x86_64
#   sudo bash deploy-remote.sh --rollback
#
# 由 Tools/deploy/deploy.ps1 上传后自动调用；也可以手动执行。
#
# 目录约定：
#   /opt/fpsgame               软链 -> 当前版本（systemd 只认这个路径）
#   /opt/fpsgame-<时间戳>       每个版本一个独立目录，回滚 = 切软链
#   /var/log/fpsgame/          服务端日志
# ===========================================================================
set -euo pipefail

APP_LINK="/opt/fpsgame"
APP_BASE="/opt/fpsgame"
LOG_DIR="/var/log/fpsgame"
RUN_USER="game"
SERVICE="fpsgame"
KEEP=5
PORT=7777
TAR=""
UNIT=""
EXE="FPSGame-Y1.x86_64"
ACTION="deploy"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --tar)      TAR="${2:?}";      shift 2 ;;
    --unit)     UNIT="${2:?}";     shift 2 ;;
    --exe)      EXE="${2:?}";      shift 2 ;;
    --port)     PORT="${2:?}";     shift 2 ;;
    --keep)     KEEP="${2:?}";     shift 2 ;;
    --rollback) ACTION="rollback"; shift ;;
    -h|--help)  sed -n '2,16p' "$0"; exit 0 ;;
    *) echo "未知参数：$1" >&2; exit 2 ;;
  esac
done

if [[ $EUID -ne 0 ]]; then
  echo "请用 sudo 运行：sudo bash $0 $*" >&2
  exit 1
fi

# 运行账号 + 日志目录
ensure_runtime() {
  getent group "$RUN_USER" >/dev/null || groupadd --system "$RUN_USER"
  id -u "$RUN_USER" >/dev/null 2>&1 || useradd -r -g "$RUN_USER" -s /usr/sbin/nologin "$RUN_USER"
  mkdir -p "$LOG_DIR"
  chown "$RUN_USER:$RUN_USER" "$LOG_DIR"
}

# 首次使用时，把手动部署留下的真实目录 /opt/fpsgame 迁移成版本目录，
# 这样软链才能正常创建（ln 遇到真实目录会把链接建到目录里面去）。
migrate_existing() {
  if [[ -e "$APP_LINK" && ! -L "$APP_LINK" ]]; then
    local old="${APP_BASE}-manual-$(date +%Y%m%d-%H%M%S)"
    echo "检测到 $APP_LINK 是真实目录（手动部署遗留），迁移为 $old"
    mv "$APP_LINK" "$old"
  fi
}

install_unit() {
  [[ -n "$UNIT" && -f "$UNIT" ]] || return 0
  sed -i 's/\r$//' "$UNIT"          # 防止 CRLF 让 systemd 解析失败
  install -m 644 "$UNIT" "/etc/systemd/system/${SERVICE}.service"
  systemctl daemon-reload
  echo "已更新 /etc/systemd/system/${SERVICE}.service"
}

current_version() { readlink -f "$APP_LINK" 2>/dev/null || true; }

rollback() {
  local prev
  prev="$(ls -1dt "${APP_BASE}"-* 2>/dev/null | sed -n '2p' || true)"
  if [[ -z "$prev" ]]; then
    echo "没有可回滚的历史版本（${APP_BASE}-* 只有当前一个）" >&2
    exit 1
  fi
  echo "回滚：$(current_version) -> $prev"
  ln -sfn "$prev" "$APP_LINK"
  systemctl restart "$SERVICE"
  sleep 2
  systemctl is-active "$SERVICE"
  echo "回滚完成，当前版本：$(current_version)"
}

deploy() {
  [[ -n "$TAR" && -f "$TAR" ]] || { echo "找不到部署包：$TAR" >&2; exit 1; }

  local new
  new="${APP_BASE}-$(date +%Y%m%d-%H%M%S)"
  echo "解压到 $new"
  mkdir -p "$new"
  tar -xzf "$TAR" -C "$new"

  if [[ ! -f "$new/$EXE" ]]; then
    echo "部署包里没有 $EXE，实际内容：" >&2
    ls -l "$new" >&2
    exit 1
  fi

  chmod +x "$new/$EXE"
  chown -R "$RUN_USER:$RUN_USER" "$new"

  ln -sfn "$new" "$APP_LINK"
  echo "软链已切换：$APP_LINK -> $(current_version)"

  systemctl restart "$SERVICE"
  sleep 2

  if ! systemctl is-active --quiet "$SERVICE"; then
    echo "服务未起来，日志尾部：" >&2
    tail -n 30 "$LOG_DIR/server.log" 2>/dev/null >&2 || true
    exit 1
  fi
  echo "服务状态：active"

  if ss -lunp 2>/dev/null | grep -q ":${PORT}"; then
    echo "端口检查：UDP ${PORT} 正在监听"
  else
    echo "警告：没有看到 UDP ${PORT} 监听，检查 $LOG_DIR/server.log" >&2
  fi

  if [[ "$KEEP" -gt 0 ]]; then
    ls -1dt "${APP_BASE}"-* 2>/dev/null | tail -n +$((KEEP + 1)) | xargs -r rm -rf
    echo "保留的版本："
    ls -1dt "${APP_BASE}"-* 2>/dev/null | sed 's/^/  /' || true
  fi

  rm -f "$TAR"
  echo
  echo "完成。常用命令："
  echo "  tail -f $LOG_DIR/server.log        看实时日志"
  echo "  sudo systemctl restart $SERVICE    重启"
  echo "  sudo systemctl status $SERVICE     状态"
}

ensure_runtime
migrate_existing
install_unit

case "$ACTION" in
  deploy)   deploy ;;
  rollback) rollback ;;
esac
