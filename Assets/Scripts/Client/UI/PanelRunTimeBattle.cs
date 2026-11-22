// 在战斗界面用准星跟随鼠标位置
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public sealed class PanelRunTimeBattle : MonoBehaviour {
    // 鼠标进入战斗区域时显示的准星
    [SerializeField] private Image crosshair;

    private Canvas canvas;
    private bool isCrosshairVisible;
    // 保存接管鼠标前的系统光标状态
    private bool previousCursorVisibility;

    // 缓存战斗界面的画布
    private void Awake() {
        TryGetComponent(out canvas);
    }

    // 根据连接、窗口和鼠标状态刷新准星
    private void LateUpdate() {
        // 检查本地玩家、窗口焦点和指针显示所需组件
        Mouse mouse = Mouse.current;
        if (!NetworkClient.isConnected || NetworkClient.localPlayer == null ||
            !Application.isFocused || !canvas.isActiveAndEnabled || crosshair == null || !crosshair.enabled ||
            mouse == null || !mouse.enabled) {
            HideCrosshair();
            return;
        }

        // 将画布内的鼠标位置换算为准星位置
        Vector2 screenPosition = mouse.position.ReadValue();
        if (!canvas.pixelRect.Contains(screenPosition) ||
            !RectTransformUtility.ScreenPointToWorldPointInRectangle(
                (RectTransform)transform, screenPosition, null, out Vector3 worldPosition)) {
            HideCrosshair();
            return;
        }

        // 更新准星位置，首次显示时隐藏系统光标
        crosshair.rectTransform.position = worldPosition;
        if (!isCrosshairVisible) {
            previousCursorVisibility = Cursor.visible;
            isCrosshairVisible = true;
            crosshair.gameObject.SetActive(true);
            Cursor.visible = false;
        }
    }

    // 窗口失焦时隐藏准星
    private void OnApplicationFocus(bool hasFocus) {
        if (!hasFocus) {
            HideCrosshair();
        }
    }

    // 界面禁用时隐藏准星
    private void OnDisable() {
        HideCrosshair();
    }

    // 隐藏准星并恢复原来的系统光标状态
    private void HideCrosshair() {
        if (crosshair != null && crosshair.gameObject.activeSelf) {
            crosshair.gameObject.SetActive(false);
        }

        if (isCrosshairVisible) {
            Cursor.visible = previousCursorVisibility;
            isCrosshairVisible = false;
        }
    }
}
