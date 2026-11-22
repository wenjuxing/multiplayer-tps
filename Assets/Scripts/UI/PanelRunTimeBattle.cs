using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public sealed class PanelRunTimeBattle : MonoBehaviour {
    [SerializeField] private Image crosshair;

    private Canvas canvas;
    private bool isCrosshairVisible;
    private bool previousCursorVisibility;

    private void Awake() {
        canvas = GetComponent<Canvas>();
    }

    private void LateUpdate() {
        if (!NetworkClient.isConnected || NetworkClient.localPlayer == null ||
            !Application.isFocused || !canvas.isActiveAndEnabled || crosshair == null || !crosshair.enabled) {
            HideCrosshair();
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.enabled) {
            HideCrosshair();
            return;
        }

        Vector2 screenPosition = mouse.position.ReadValue();
        if (!canvas.pixelRect.Contains(screenPosition) ||
            !RectTransformUtility.ScreenPointToWorldPointInRectangle(
                (RectTransform)transform, screenPosition, null, out Vector3 worldPosition)) {
            HideCrosshair();
            return;
        }

        crosshair.rectTransform.position = worldPosition;
        if (!isCrosshairVisible) {
            previousCursorVisibility = Cursor.visible;
            isCrosshairVisible = true;
            crosshair.gameObject.SetActive(true);
            Cursor.visible = false;
        }
    }

    private void OnApplicationFocus(bool hasFocus) {
        if (!hasFocus) {
            HideCrosshair();
        }
    }

    private void OnDisable() {
        HideCrosshair();
    }

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
